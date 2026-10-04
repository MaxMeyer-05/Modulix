using System.Formats.Tar;
using System.Net;
using System.Text;
using System.Text.Json;

using Docker.DotNet;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Server.Services;

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

    [Fact]
    public async Task WaitUntilReadyAsync_Cancellation_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var service = CreateService((request, cancellationToken) =>
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(JsonResponse(new { }));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.WaitUntilReadyAsync("candidate", cancellation.Token));
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildContainerAsync_VersionedBuild_ChecksErrorsAndBuildContext(bool buildFails)
    {
        var root = Path.Combine(Path.GetTempPath(), "Modulix_Docker_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var moduleId = Guid.NewGuid();
        string? createdImage = null;
        string? createdName = null;
        var createdContainer = false;
        var deletedImage = false;
        var archiveEntries = new List<string>();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "module.dll"), "module contents");
            using var service = CreateService(async (request, cancellationToken) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/networks", StringComparison.Ordinal))
                    return JsonResponse(new[] { new { Name = "modulix-network" } });

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
                    return JsonResponse(new { Id = "candidate" });
                }

                deletedImage |= request.Method == HttpMethod.Delete && path.Contains("/images/", StringComparison.Ordinal);
                return JsonResponse(Array.Empty<object>());
            });

            if (buildFails)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.BuildContainerAsync(moduleId, root, "module.dll", 8080));
                Assert.False(createdContainer);
                Assert.True(deletedImage);
            }
            else
            {
                Assert.Equal("candidate", await service.BuildContainerAsync(moduleId, root, "module.dll", 8080));
                Assert.StartsWith($"modulix-module-{moduleId:N}:", createdImage);
                Assert.StartsWith($"modulix-container-{moduleId:N}-", createdName);
                Assert.False(deletedImage);
            }

            Assert.Contains("Dockerfile", archiveEntries);
            Assert.Contains("module.dll", archiveEntries);
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