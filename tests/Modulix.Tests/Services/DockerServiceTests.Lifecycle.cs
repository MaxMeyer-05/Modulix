using System.Formats.Tar;
using System.Net;
using System.Text;
using System.Text.Json;

using Docker.DotNet;
using Microsoft.AspNetCore.WebUtilities;

using Modulix.Docker.Options;
using Modulix.Tests.TestData;

namespace Modulix.Tests.Services;

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
            await service.RemoveContainerAsync("missing", Guid.NewGuid());
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

        await service.RemoveContainerAsync("old-container", Guid.NewGuid());

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

        await service.RemoveContainerAsync("old-container", Guid.NewGuid());

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

        await Assert.ThrowsAsync<DockerApiException>(() => service.RemoveContainerAsync("old-container", Guid.NewGuid()));

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
            service.RemoveContainerAsync("old-container", Guid.NewGuid()));

        Assert.Contains("Failure during " + failingOperation, exception.Message);
        Assert.Equal(expectedRequests, requests.Count);
        if (failingOperation == "stop")
            Assert.DoesNotContain(requests, request => request.StartsWith("DELETE ", StringComparison.Ordinal));
    }

    #endregion

    #region BuildContainer Tests

    [Theory]
    [InlineData(false, false, 8080, "modulix-network", "mcr.microsoft.com/dotnet/aspnet:10.0")]
    [InlineData(true, false, 8080, "modulix-network", "mcr.microsoft.com/dotnet/aspnet:10.0")]
    [InlineData(false, true, 8080, "modulix-network", "mcr.microsoft.com/dotnet/aspnet:10.0")]
    [InlineData(false, false, 5500, "modulix-network", "mcr.microsoft.com/dotnet/aspnet:10.0")]
    [InlineData(false, true, 8080, "custom-network", "custom-runtime:latest")]
    public async Task BuildContainerAsync_VersionedBuild_ChecksErrorsAndBuildContext(
        bool buildFails, bool createNetwork, int containerPort, string networkName, string baseImage)
    {
        var root = Path.Combine(Path.GetTempPath(), "Modulix_Docker_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var moduleId = Guid.NewGuid();
        string? createdImage = null;
        string? createdName = null;
        var createdContainer = false;
        var deletedImage = false;
        var networkCreated = false;
        var archiveEntries = new List<string>();
        var archiveContents = new Dictionary<string, string>();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "module.dll"), "module contents");
            using var service = CreateService(async (request, cancellationToken) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/networks", StringComparison.Ordinal))
                {
                    return DockerHttpTestHelper.JsonResponse(createNetwork
                        ? Array.Empty<object>()
                        : new[] { new { Name = networkName } });
                }

                if (path.EndsWith("/networks/create", StringComparison.Ordinal))
                {
                    networkCreated = true;
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    Assert.Equal(networkName, body.RootElement.GetProperty("Name").GetString());
                    Assert.Equal("bridge", body.RootElement.GetProperty("Driver").GetString());
                    return DockerHttpTestHelper.JsonResponse(new { Id = "network" });
                }

                if (path.EndsWith("/build", StringComparison.Ordinal))
                {
                    await using var context = await request.Content!.ReadAsStreamAsync(cancellationToken);
                    using var reader = new TarReader(context);
                    while (reader.GetNextEntry() is { } entry)
                    {
                        var entryName = entry.Name.TrimStart('.', '/');
                        archiveEntries.Add(entryName);
                        if (entry.DataStream is not null)
                        {
                            using var contents = new StreamReader(entry.DataStream, leaveOpen: true);
                            archiveContents[entryName] = await contents.ReadToEndAsync(cancellationToken);
                        }
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
                    Assert.Equal($"{Path.Combine(Path.GetFullPath(root), "data")}:/app/data", boundVolume.GetString());
                    Assert.False(hostConfig.TryGetProperty("PublishAllPorts", out var publishAllPorts) && publishAllPorts.GetBoolean());
                    var exposedPort = Assert.Single(body.RootElement.GetProperty("ExposedPorts").EnumerateObject());
                    Assert.Equal($"{containerPort}/tcp", exposedPort.Name);
                    var boundPort = Assert.Single(hostConfig.GetProperty("PortBindings").EnumerateObject());
                    Assert.Equal($"{containerPort}/tcp", boundPort.Name);
                    var binding = Assert.Single(boundPort.Value.EnumerateArray());
                    Assert.Equal("127.0.0.1", binding.GetProperty("HostIp").GetString());
                    Assert.Equal($"{containerPort}", binding.GetProperty("HostPort").GetString());
                    Assert.Contains($"ASPNETCORE_HTTP_PORTS={containerPort}", body.RootElement.GetProperty("Env").EnumerateArray().Select(value => value.GetString()));
                    return DockerHttpTestHelper.JsonResponse(new { Id = "candidate" });
                }

                if (buildFails && request.Method == HttpMethod.Delete && path.Contains("/containers/", StringComparison.Ordinal))
                    return DockerHttpTestHelper.JsonResponse(new { message = "No such container." }, HttpStatusCode.NotFound);

                deletedImage |= request.Method == HttpMethod.Delete && path.Contains("/images/", StringComparison.Ordinal);
                return DockerHttpTestHelper.JsonResponse(Array.Empty<object>());
            }, new DockerOptions { NetworkName = networkName, BaseImage = baseImage });

            if (buildFails)
            {
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.BuildContainerAsync(moduleId, root, "module.dll", containerPort));
                Assert.Contains("Build failed.", exception.Message);
                Assert.False(createdContainer);
                Assert.True(deletedImage);
            }
            else
            {
                Assert.Equal("candidate", await service.BuildContainerAsync(moduleId, root, "module.dll", containerPort));
                Assert.StartsWith($"modulix-module-{moduleId:N}:", createdImage);
                Assert.StartsWith($"modulix-container-{moduleId:N}-", createdName);
                Assert.False(deletedImage);
            }

            Assert.Contains("Dockerfile", archiveEntries);
            Assert.Contains("module.dll", archiveEntries);
            Assert.Equal("module contents", archiveContents["module.dll"]);
            Assert.Equal(createNetwork, networkCreated);
            var dockerfile = await File.ReadAllTextAsync(Path.Combine(root, "Dockerfile"));
            Assert.StartsWith($"FROM {baseImage}", dockerfile.TrimStart());
            Assert.Equal(dockerfile, archiveContents["Dockerfile"]);
            var entrypoint = Assert.Single(dockerfile.Split('\n'), line => line.TrimStart().StartsWith("ENTRYPOINT ", StringComparison.Ordinal));
            Assert.Equal(new[] { "dotnet", "module.dll" }, JsonSerializer.Deserialize<string[]>(entrypoint.Trim()["ENTRYPOINT ".Length..]));
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
        var root = Path.Combine(Path.GetTempPath(), "Modulix_Docker_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cancellation = new CancellationTokenSource();
        var cleanupRequests = new List<string>();
        try
        {
            using var service = CreateService((request, cancellationToken) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/networks", StringComparison.Ordinal))
                    return Task.FromResult(DockerHttpTestHelper.JsonResponse(new[] { new { Name = "modulix-network" } }));

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
                    service.BuildContainerAsync(Guid.NewGuid(), root, "module.dll", 8080, cancellation.Token));
            }
            else
            {
                var failure = await Assert.ThrowsAsync<DockerApiException>(() =>
                    service.BuildContainerAsync(Guid.NewGuid(), root, "module.dll", 8080));
                Assert.Contains("Candidate creation failed.", failure.Message);
            }

            Assert.Equal(2, cleanupRequests.Count);
            Assert.Contains("/containers/modulix-container-", cleanupRequests[0]);
            Assert.Contains("/images/modulix-module-", cleanupRequests[1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildContainerAsync_VersionedModuleStoragePath_MountsSharedPersistentDataDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "Modulix_Docker_Tests_" + Guid.NewGuid().ToString("N"));
        var moduleId = Guid.NewGuid();
        var v1Path = Path.Combine(root, moduleId.ToString(), "versions", "v1.0.0");
        var v2Path = Path.Combine(root, moduleId.ToString(), "versions", "v1.1.0");
        var expectedDataPath = Path.Combine(Path.GetFullPath(root), moduleId.ToString(), "data");
        Directory.CreateDirectory(v1Path);
        Directory.CreateDirectory(v2Path);
        var recordedBinds = new List<string>();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(v1Path, "module.dll"), "v1");
            await File.WriteAllTextAsync(Path.Combine(v2Path, "module.dll"), "v2");

            using var service = CreateService(async (request, cancellationToken) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/networks", StringComparison.Ordinal))
                    return DockerHttpTestHelper.JsonResponse(new[] { new { Name = "modulix-network" } });

                if (path.EndsWith("/build", StringComparison.Ordinal))
                    return DockerHttpTestHelper.JsonResponse(new { stream = "Build complete.\n" });

                if (path.EndsWith("/containers/create", StringComparison.Ordinal))
                {
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    var bind = Assert.Single(body.RootElement.GetProperty("HostConfig").GetProperty("Binds").EnumerateArray());
                    recordedBinds.Add(bind.GetString()!);
                    return DockerHttpTestHelper.JsonResponse(new { Id = "candidate-" + recordedBinds.Count });
                }

                return DockerHttpTestHelper.JsonResponse(Array.Empty<object>());
            });

            await service.BuildContainerAsync(moduleId, v1Path, "module.dll", 8080);
            Assert.True(Directory.Exists(expectedDataPath));

            var dbFilePath = Path.Combine(expectedDataPath, "app.db");
            await File.WriteAllTextAsync(dbFilePath, "sqlite-data");

            await service.BuildContainerAsync(moduleId, v2Path, "module.dll", 8080);

            Assert.Equal([$"{expectedDataPath}:/app/data", $"{expectedDataPath}:/app/data"], recordedBinds);
            Assert.True(File.Exists(dbFilePath));
            Assert.Equal("sqlite-data", await File.ReadAllTextAsync(dbFilePath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    #endregion
}
