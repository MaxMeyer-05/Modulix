using System.Formats.Tar;
using System.Net;
using System.Text;
using System.Text.Json;

using Docker.DotNet;
using Microsoft.AspNetCore.WebUtilities;

using Modulix.Docker.Tests.TestData;

namespace Modulix.Docker.Tests.Services;

/// <summary>
/// Container lifecycle, build, and cleanup tests for <see cref="Modulix.Docker.Services.DockerService"/>.
/// </summary>
public partial class DockerServiceTests
{
    #region Container Lifecycle Tests

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotModified)]
    public async Task RunContainerAsync_NewOrAlreadyRunningContainer_Succeeds(HttpStatusCode status)
    {
        var requests = 0;
        using var service = CreateService((request, cancellationToken) =>
        {
            requests++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/containers/candidate/start", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(status));
        });

        await service.RunContainerAsync("candidate");

        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("stop")]
    public async Task CleanupAsync_MissingContainer_IsIdempotent(string operation)
    {
        var requests = 0;
        using var service = CreateService((request, cancellationToken) =>
        {
            requests++;
            Assert.EndsWith(operation == "remove" ? "/containers/missing/json" : "/containers/missing/stop",
                request.RequestUri!.AbsolutePath);
            return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "No such container." }, HttpStatusCode.NotFound));
        });

        if (operation == "remove")
            await service.RemoveContainerAsync("missing", "missing-container");
        else
            await service.StopContainerAsync("missing");

        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    public async Task StopContainerAsync_StopThrowsDockerApiException_SucceedsOnlyWhenContainerStoppedOrRemoved(
        bool stillRunning, bool restarting, bool inspectNotFound, bool expectedSuccess)
    {
        var requests = new List<string>();
        using var service = CreateService((request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Add(request.Method + " " + path);

            if (path.EndsWith("/stop", StringComparison.Ordinal))
            {
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(
                    new { cause = "permission denied", message = "rootless netns: kill network process: permission denied" },
                    HttpStatusCode.InternalServerError));
            }

            if (inspectNotFound)
            {
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(
                    new { message = "No such container." },
                    HttpStatusCode.NotFound));
            }

            return Task.FromResult(DockerHttpTestHelper.JsonResponse(new
            {
                State = new { Running = stillRunning, Restarting = restarting }
            }));
        });

        if (expectedSuccess)
        {
            await service.StopContainerAsync("podman-container");
        }
        else
        {
            var ex = await Assert.ThrowsAsync<DockerApiException>(() =>
                service.StopContainerAsync("podman-container"));
            Assert.Contains("rootless netns", ex.Message);
        }

        Assert.Equal(2, requests.Count);
        Assert.EndsWith("/containers/podman-container/stop", requests[0]);
        Assert.EndsWith("/containers/podman-container/json", requests[1]);
    }

    [Fact]
    public async Task RemoveContainerAsync_VersionedContainer_DeletesOnlyItsImageWithForce()
    {
        var requests = new List<string>();
        string? imageDeleteQuery = null;
        const string oldImage = "modulix-module-123:old-version";
        using var service = CreateService((request, cancellationToken) =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            requests.Add(request.Method + " " + path);
            if (path.Contains("/images/", StringComparison.Ordinal))
                imageDeleteQuery = request.RequestUri.Query;

            return Task.FromResult(path.EndsWith("/json", StringComparison.Ordinal)
                ? DockerHttpTestHelper.JsonResponse(new { Config = new { Image = oldImage } })
                : DockerHttpTestHelper.JsonResponse(Array.Empty<object>()));
        });

        await service.RemoveContainerAsync("old-container", "container-123");

        Assert.Equal(4, requests.Count);
        Assert.EndsWith("/containers/old-container/json", requests[0]);
        Assert.EndsWith("/containers/old-container/stop", requests[1]);
        Assert.EndsWith("/containers/old-container", requests[2]);
        Assert.EndsWith("/images/" + oldImage, requests[3]);
        Assert.Equal("1", QueryHelpers.ParseQuery(imageDeleteQuery)["force"].ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task RemoveContainerAsync_ImageDeletionFailsOrConflicts_CompletesWithoutThrowing(HttpStatusCode imageDeleteStatus)
    {
        var requests = new List<string>();
        const string imageName = "modulix-module-123:in-use";
        using var service = CreateService((request, cancellationToken) =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            requests.Add(request.Method + " " + path);
            if (path.EndsWith("/json", StringComparison.Ordinal))
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { Config = new { Image = imageName } }));

            if (path.Contains("/images/", StringComparison.Ordinal))
            {
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(
                    new { message = "Image is in use by a container." },
                    imageDeleteStatus));
            }

            return Task.FromResult(DockerHttpTestHelper.JsonResponse(Array.Empty<object>()));
        });

        await service.RemoveContainerAsync("old-container", "container-123");

        Assert.Equal(4, requests.Count);
        Assert.EndsWith("/containers/old-container", requests[2]);
        Assert.EndsWith("/images/" + imageName, requests[3]);
    }

    [Fact]
    public async Task RemoveContainerAsync_RemovalFails_DoesNotDeleteImage()
    {
        var imageDeleted = false;
        using var service = CreateService((request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/json", StringComparison.Ordinal))
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { Config = new { Image = "old-image" } }));

            if (request.Method == HttpMethod.Delete && path.Contains("/containers/", StringComparison.Ordinal))
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "Removal failed." }, HttpStatusCode.InternalServerError));

            imageDeleted |= path.Contains("/images/", StringComparison.Ordinal);
            return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { }));
        });

        await Assert.ThrowsAsync<DockerApiException>(() => service.RemoveContainerAsync("old-container", "container-123"));

        Assert.False(imageDeleted);
    }

    [Theory]
    [InlineData("inspect", 1)]
    [InlineData("stop", 3)]
    [InlineData("container", 3)]
    public async Task RemoveContainerAsync_DockerFailure_StopsCleanupAtFailedOperation(string failingOperation, int expectedRequests)
    {
        var requests = new List<string>();
        using var service = CreateService((request, cancellationToken) =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            requests.Add(request.Method + " " + path);
            var operation = path.EndsWith("/json", StringComparison.Ordinal) ? "inspect"
                : path.EndsWith("/stop", StringComparison.Ordinal) ? "stop"
                : path.Contains("/images/", StringComparison.Ordinal) ? "image" : "container";
            if (operation == failingOperation)
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "Failure during " + operation }, HttpStatusCode.InternalServerError));
            return Task.FromResult(operation == "inspect"
                ? DockerHttpTestHelper.JsonResponse(new { Config = new { Image = "module-image:old" }, State = new { Running = true, Restarting = false } })
                : DockerHttpTestHelper.JsonResponse(Array.Empty<object>()));
        });

        var exception = await Assert.ThrowsAsync<DockerApiException>(() =>
            service.RemoveContainerAsync("old-container", "container-123"));

        Assert.Contains("Failure during " + failingOperation, exception.Message);
        Assert.Equal(expectedRequests, requests.Count);
        if (failingOperation == "stop")
            Assert.DoesNotContain(requests, request => request.StartsWith("DELETE ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryRemoveModuleContainerAsync_ReturnsTrueOnSuccess_AndFalseOnException()
    {
        var shouldThrow = false;
        using var service = CreateService((request, cancellationToken) =>
        {
            if (shouldThrow)
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "Failed." }, HttpStatusCode.InternalServerError));

            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            return Task.FromResult(path.EndsWith("/json", StringComparison.Ordinal)
                ? DockerHttpTestHelper.JsonResponse(new { Config = new { Image = "img" } })
                : DockerHttpTestHelper.JsonResponse(Array.Empty<object>()));
        });

        var success = await service.TryRemoveModuleContainerAsync("cont1", "name1");
        Assert.True(success);

        shouldThrow = true;
        var failed = await service.TryRemoveModuleContainerAsync("cont1", "name1");
        Assert.False(failed);
    }

    [Fact]
    public async Task EnsureNetworkExistsAsync_NetworkDoesNotExist_CreatesNetwork()
    {
        var networkCreated = false;
        using var service = CreateService(async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/networks", StringComparison.Ordinal))
            {
                return DockerHttpTestHelper.JsonResponse(Array.Empty<object>());
            }

            if (path.EndsWith("/networks/create", StringComparison.Ordinal))
            {
                networkCreated = true;
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal("test-net", doc.RootElement.GetProperty("Name").GetString());
                Assert.Equal("bridge", doc.RootElement.GetProperty("Driver").GetString());
                return DockerHttpTestHelper.JsonResponse(new { Id = "net-1" });
            }

            return DockerHttpTestHelper.JsonResponse(new { });
        });

        await service.EnsureNetworkExistsAsync("test-net");

        Assert.True(networkCreated);
    }

    [Fact]
    public async Task EnsureNetworkExistsAsync_NetworkAlreadyExists_DoesNotCreateNetwork()
    {
        var networkCreated = false;
        using var service = CreateService((request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/networks", StringComparison.Ordinal))
            {
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new[] { new { Name = "test-net" } }));
            }

            if (path.EndsWith("/networks/create", StringComparison.Ordinal))
            {
                networkCreated = true;
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { Id = "net-1" }));
            }

            return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { }));
        });

        await service.EnsureNetworkExistsAsync("test-net");

        Assert.False(networkCreated);
    }

    #endregion

    #region BuildContainer Tests

    [Fact]
    public async Task BuildContainerAsync_MissingDockerfile_ThrowsFileNotFoundException()
    {
        var root = Path.Combine(Path.GetTempPath(), "Modulix_Docker_Build_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var service = CreateService((_, _) => Task.FromResult(DockerHttpTestHelper.JsonResponse(new { })));

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                service.BuildContainerAsync(root, "Dockerfile", "img", "cont", "net", 8080));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, 8080, "test-network")]
    [InlineData(true, 8080, "test-network")]
    [InlineData(false, 5500, "custom-network")]
    public async Task BuildContainerAsync_ValidBuild_BuildsImageAndCreatesContainer(
        bool buildFails, int containerPort, string networkName)
    {
        var root = Path.Combine(Path.GetTempPath(), "Modulix_Docker_Build_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var createdContainer = false;
        var deletedImage = false;
        string? createdImage = null;
        string? createdName = null;
        var archiveEntries = new List<string>();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Dockerfile"), "FROM mcr.microsoft.com/dotnet/aspnet:10.0\nWORKDIR /app");
            await File.WriteAllTextAsync(Path.Combine(root, "app.dll"), "dll contents");

            using var service = CreateService(async (request, cancellationToken) =>
            {
                var path = request.RequestUri!.AbsolutePath;

                if (path.EndsWith("/build", StringComparison.Ordinal))
                {
                    await using var context = await request.Content!.ReadAsStreamAsync(cancellationToken);
                    using var reader = new TarReader(context);
                    while (reader.GetNextEntry() is { } entry)
                    {
                        var entryName = entry.Name.TrimStart('.', '/');
                        archiveEntries.Add(entryName);
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(buildFails
                            ? "{\"error\":\"Build failed.\"}\n{\"stream\":\"Build complete.\"}\n"
                            : "{\"stream\":\"Build complete.\"}\n", Encoding.UTF8, "application/json")
                    };
                }

                if (path.EndsWith("/containers/create", StringComparison.Ordinal))
                {
                    createdContainer = true;
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    createdImage = body.RootElement.GetProperty("Image").GetString();
                    createdName = QueryHelpers.ParseQuery(request.RequestUri.Query)["name"].ToString();
                    var hostConfig = body.RootElement.GetProperty("HostConfig");
                    Assert.Equal(networkName, hostConfig.GetProperty("NetworkMode").GetString());
                    var boundVolume = Assert.Single(hostConfig.GetProperty("Binds").EnumerateArray());
                    Assert.Equal($"{root}/data:/app/data", boundVolume.GetString());
                    Assert.False(hostConfig.TryGetProperty("PublishAllPorts", out var publishAllPorts) && publishAllPorts.GetBoolean());
                    var exposedPort = Assert.Single(body.RootElement.GetProperty("ExposedPorts").EnumerateObject());
                    Assert.Equal($"{containerPort}/tcp", exposedPort.Name);
                    var boundPort = Assert.Single(hostConfig.GetProperty("PortBindings").EnumerateObject());
                    Assert.Equal($"{containerPort}/tcp", boundPort.Name);
                    var binding = Assert.Single(boundPort.Value.EnumerateArray());
                    Assert.Equal("127.0.0.1", binding.GetProperty("HostIp").GetString());
                    Assert.Equal($"{containerPort}", binding.GetProperty("HostPort").GetString());
                    Assert.Contains($"PORT={containerPort}", body.RootElement.GetProperty("Env").EnumerateArray().Select(v => v.GetString()));
                    return DockerHttpTestHelper.JsonResponse(new { Id = "candidate" });
                }

                if (buildFails && request.Method == HttpMethod.Delete && path.Contains("/containers/", StringComparison.Ordinal))
                    return DockerHttpTestHelper.JsonResponse(new { message = "No such container." }, HttpStatusCode.NotFound);

                deletedImage |= request.Method == HttpMethod.Delete && path.Contains("/images/", StringComparison.Ordinal);
                return DockerHttpTestHelper.JsonResponse(Array.Empty<object>());
            });

            var env = new[] { $"PORT={containerPort}" };
            var binds = new[] { $"{root}/data:/app/data" };

            if (buildFails)
            {
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.BuildContainerAsync(root, "Dockerfile", "test-image:v1", "test-cont-1", networkName, containerPort, env, binds));
                Assert.Contains("Build failed.", exception.Message);
                Assert.False(createdContainer);
                Assert.True(deletedImage);
            }
            else
            {
                var result = await service.BuildContainerAsync(root, "Dockerfile", "test-image:v1", "test-cont-1", networkName, containerPort, env, binds);
                Assert.Equal("candidate", result);
                Assert.Equal("test-image:v1", createdImage);
                Assert.Equal("test-cont-1", createdName);
                Assert.False(deletedImage);
            }

            Assert.Contains("Dockerfile", archiveEntries);
            Assert.Contains("app.dll", archiveEntries);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildContainerAsync_CreationAndCleanupFail_PreservesOriginalFailure(bool callerCancels)
    {
        var root = Path.Combine(Path.GetTempPath(), "Modulix_Docker_Build_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cancellation = new CancellationTokenSource();
        var cleanupRequests = new List<string>();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Dockerfile"), "FROM alpine");

            using var service = CreateService((request, cancellationToken) =>
            {
                var path = request.RequestUri!.AbsolutePath;

                if (path.EndsWith("/build", StringComparison.Ordinal))
                    return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { stream = "Build complete.\n" }));

                if (path.EndsWith("/containers/create", StringComparison.Ordinal))
                {
                    if (callerCancels)
                    {
                        cancellation.Cancel();
                        return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
                    }
                    return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "Candidate creation failed." }, HttpStatusCode.InternalServerError));
                }

                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.False(cancellationToken.IsCancellationRequested);
                cleanupRequests.Add(path);
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "Cleanup also failed." }, HttpStatusCode.InternalServerError));
            });

            if (callerCancels)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    service.BuildContainerAsync(root, "Dockerfile", "test-image:v1", "test-cont-1", "modulix-network", 8080, ct: cancellation.Token));
            }
            else
            {
                var failure = await Assert.ThrowsAsync<DockerApiException>(() =>
                    service.BuildContainerAsync(root, "Dockerfile", "test-image:v1", "test-cont-1", "modulix-network", 8080));
                Assert.Contains("Candidate creation failed.", failure.Message);
            }

            Assert.Equal(2, cleanupRequests.Count);
            Assert.Contains("/containers/test-cont-1", cleanupRequests[0]);
            Assert.Contains("/images/test-image:v1", cleanupRequests[1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    #endregion
}
