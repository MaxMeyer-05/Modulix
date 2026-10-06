using System.Formats.Tar;
using System.Net;
using System.Text;
using System.Text.Json;

using Docker.DotNet;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Services;

namespace Modulix.Tests.Services;

[Trait("Category", "Services")]
[Trait("SubCategory", "DockerService")]
public class DockerServiceTests
{
    [Theory]
    [InlineData("healthy", true, false, true)]
    [InlineData("unhealthy", true, false, false)]
    [InlineData(null, true, false, false)]
    [InlineData("healthy", false, false, false)]
    [InlineData("healthy", true, true, false)]
    public async Task WaitUntilReadyAsync_ContainerState_RequiresRunningHealthyContainer(
        string? health, bool running, bool restarting, bool expectedReady)
    {
        using var service = CreateService((request, cancellationToken) =>
        {
            Assert.EndsWith("/containers/candidate/json", request.RequestUri!.AbsolutePath);
            return Task.FromResult(JsonResponse(new
            {
                State = new { Running = running, Restarting = restarting, Health = new { Status = health } }
            }));
        });

        if (expectedReady)
            await service.WaitUntilReadyAsync("candidate");
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.WaitUntilReadyAsync("candidate"));
    }

    [Fact]
    public async Task WaitUntilReadyAsync_StartingContainer_WaitsForHealthyState()
    {
        var inspections = 0;
        using var service = CreateService((request, cancellationToken) => Task.FromResult(JsonResponse(new
        {
            State = new
            {
                Running = true,
                Health = new { Status = ++inspections == 1 ? "starting" : "healthy" }
            }
        })));

        await service.WaitUntilReadyAsync("candidate");

        Assert.Equal(2, inspections);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WaitUntilReadyAsync_Cancellation_DistinguishesCallerCancellationAndTimeout(bool callerCancels)
    {
        using var cancellation = new CancellationTokenSource();
        using var service = CreateService((request, cancellationToken) =>
        {
            if (callerCancels)
                cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(new CancellationToken(canceled: true));
        });

        if (callerCancels)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.WaitUntilReadyAsync("candidate", cancellation.Token));
        else
            await Assert.ThrowsAsync<TimeoutException>(() => service.WaitUntilReadyAsync("candidate", cancellation.Token));
    }

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
            return Task.FromResult(JsonResponse(new { message = "No such container." }, HttpStatusCode.NotFound));
        });

        if (operation == "remove")
            await service.RemoveContainerAsync("missing", Guid.NewGuid());
        else
            await service.StopContainerAsync("missing");

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task RemoveContainerAsync_VersionedContainer_DeletesOnlyItsImage()
    {
        var requests = new List<string>();
        const string oldImage = "modulix-module-123:old-version";
        using var service = CreateService((request, cancellationToken) =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            requests.Add(request.Method + " " + path);
            return Task.FromResult(path.EndsWith("/json", StringComparison.Ordinal)
                ? JsonResponse(new { Config = new { Image = oldImage } })
                : JsonResponse(Array.Empty<object>()));
        });

        await service.RemoveContainerAsync("old-container", Guid.NewGuid());

        Assert.Equal(4, requests.Count);
        Assert.EndsWith("/containers/old-container/json", requests[0]);
        Assert.EndsWith("/containers/old-container/stop", requests[1]);
        Assert.EndsWith("/containers/old-container", requests[2]);
        Assert.EndsWith("/images/" + oldImage, requests[3]);
    }

    [Fact]
    public async Task RemoveContainerAsync_RemovalFails_DoesNotDeleteImage()
    {
        var imageDeleted = false;
        using var service = CreateService((request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/json", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse(new { Config = new { Image = "old-image" } }));

            if (request.Method == HttpMethod.Delete && path.Contains("/containers/", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse(new { message = "Removal failed." }, HttpStatusCode.InternalServerError));

            imageDeleted |= path.Contains("/images/", StringComparison.Ordinal);
            return Task.FromResult(JsonResponse(new { }));
        });

        await Assert.ThrowsAsync<DockerApiException>(() => service.RemoveContainerAsync("old-container", Guid.NewGuid()));

        Assert.False(imageDeleted);
    }

    [Theory]
    [InlineData(false, false, 8080)]
    [InlineData(true, false, 8080)]
    [InlineData(false, true, 8080)]
    [InlineData(false, false, 5500)]
    public async Task BuildContainerAsync_VersionedBuild_ChecksErrorsAndBuildContext(bool buildFails, bool createNetwork, int containerPort)
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
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "module.dll"), "module contents");
            using var service = CreateService(async (request, cancellationToken) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/networks", StringComparison.Ordinal))
                    return JsonResponse(createNetwork ? [] : new[] { new { Name = "modulix-network" } });

                if (path.EndsWith("/networks/create", StringComparison.Ordinal))
                {
                    networkCreated = true;
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    Assert.Equal("modulix-network", body.RootElement.GetProperty("Name").GetString());
                    Assert.Equal("bridge", body.RootElement.GetProperty("Driver").GetString());
                    return JsonResponse(new { Id = "network" });
                }

                if (path.EndsWith("/build", StringComparison.Ordinal))
                {
                    await using var context = await request.Content!.ReadAsStreamAsync(cancellationToken);
                    using var reader = new TarReader(context);
                    while (reader.GetNextEntry() is { } entry)
                        archiveEntries.Add(entry.Name.TrimStart('.', '/'));

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(buildFails
                            ? "{\"error\":\"Build failed.\"}\n"
                            : "{\"stream\":\"Build complete.\"}\n", Encoding.UTF8, "application/json")
                    };
                }

                if (path.EndsWith("/containers/create", StringComparison.Ordinal))
                {
                    createdContainer = true;
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    createdImage = body.RootElement.GetProperty("Image").GetString();
                    createdName = QueryHelpers.ParseQuery(request.RequestUri.Query)["name"].ToString();
                    Assert.Equal("CMD", body.RootElement.GetProperty("Healthcheck").GetProperty("Test")[0].GetString());
                    Assert.Equal(1_000_000_000, body.RootElement.GetProperty("Healthcheck").GetProperty("Interval").GetInt64());
                    Assert.Equal(2_000_000_000, body.RootElement.GetProperty("Healthcheck").GetProperty("Timeout").GetInt64());
                    var hostConfig = body.RootElement.GetProperty("HostConfig");
                    Assert.Equal("modulix-network", hostConfig.GetProperty("NetworkMode").GetString());
                    Assert.False(hostConfig.TryGetProperty("PublishAllPorts", out var publishAllPorts) && publishAllPorts.GetBoolean());
                    var exposedPort = Assert.Single(body.RootElement.GetProperty("ExposedPorts").EnumerateObject());
                    Assert.Equal($"{containerPort}/tcp", exposedPort.Name);
                    var boundPort = Assert.Single(hostConfig.GetProperty("PortBindings").EnumerateObject());
                    Assert.Equal($"{containerPort}/tcp", boundPort.Name);
                    var binding = Assert.Single(boundPort.Value.EnumerateArray());
                    Assert.Equal("127.0.0.1", binding.GetProperty("HostIp").GetString());
                    Assert.Equal("0", binding.GetProperty("HostPort").GetString());
                    Assert.Contains($"ASPNETCORE_HTTP_PORTS={containerPort}", body.RootElement.GetProperty("Env").EnumerateArray().Select(value => value.GetString()));
                    return JsonResponse(new { Id = "candidate" });
                }

                if (buildFails && request.Method == HttpMethod.Delete && path.Contains("/containers/", StringComparison.Ordinal))
                    return JsonResponse(new { message = "No such container." }, HttpStatusCode.NotFound);

                deletedImage |= request.Method == HttpMethod.Delete && path.Contains("/images/", StringComparison.Ordinal);
                return JsonResponse(Array.Empty<object>());
            });

            if (buildFails)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.BuildContainerAsync(moduleId, root, "module.dll", containerPort));
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
            Assert.Equal(createNetwork, networkCreated);
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
                    return Task.FromResult(JsonResponse(new[] { new { Name = "modulix-network" } }));

                if (path.EndsWith("/build", StringComparison.Ordinal))
                    return Task.FromResult(JsonResponse(new { stream = "Build complete.\n" }));

                if (path.EndsWith("/containers/create", StringComparison.Ordinal))
                {
                    if (callerCancels)
                    {
                        cancellation.Cancel();
                        return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
                    }
                    return Task.FromResult(JsonResponse(new { message = "Candidate creation failed." }, HttpStatusCode.InternalServerError));
                }

                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.False(cancellationToken.IsCancellationRequested);
                cleanupRequests.Add(path);
                return Task.FromResult(JsonResponse(new { message = "Cleanup also failed." }, HttpStatusCode.InternalServerError));
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

    private static DockerService CreateService(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        var credentials = new FakeCredentials(new FakeDockerHandler(responder));
        var client = new DockerClientConfiguration(new Uri("http://docker.test"), credentials).CreateClient();
        return new DockerService(NullLogger<DockerService>.Instance, client);
    }

    private static HttpResponseMessage JsonResponse(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private sealed class FakeDockerHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request, cancellationToken);
    }

    private sealed class FakeCredentials(HttpMessageHandler handler) : Credentials
    {
        public override bool IsTlsCredentials() => false;

        public override HttpMessageHandler GetHandler(HttpMessageHandler innerHandler)
        {
            innerHandler.Dispose();
            return handler;
        }
    }
}