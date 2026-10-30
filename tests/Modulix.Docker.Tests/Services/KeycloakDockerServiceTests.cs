using System.Net;

using Docker.DotNet;
using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Docker.Models;
using Modulix.Docker.Services;
using Modulix.Docker.Services.Interfaces;
using Modulix.Docker.Tests.TestData;

namespace Modulix.Docker.Tests.Services;

[Trait("Category", "Services")]
[Trait("SubCategory", "KeycloakDockerService")]
public class KeycloakDockerServiceTests
{
    [Fact]
    public async Task WaitUntilReadyAsync_WhenDiscoveryEndpointReturns200_ReturnsTrue()
    {
        var fakeDockerService = new FakeInternalDockerService();
        var client = DockerHttpTestHelper.CreateDockerClient((_, _) => Task.FromResult(DockerHttpTestHelper.JsonResponse(new { })));
        var handler = new FakeHttpHandler((request, _) =>
        {
            Assert.Contains("/.well-known/openid-configuration", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var httpFactory = new SimpleHttpClientFactory(new HttpClient(handler));

        var sut = new KeycloakDockerService(client, fakeDockerService, httpFactory, NullLogger<KeycloakDockerService>.Instance);

        var ready = await sut.WaitUntilReadyAsync("http://localhost:8080/auth/realms/modulix", TimeSpan.FromSeconds(5));

        Assert.True(ready);
    }

    [Fact]
    public async Task WaitUntilReadyAsync_WhenDiscoveryEndpointTimesOut_ReturnsFalse()
    {
        var fakeDockerService = new FakeInternalDockerService();
        var client = DockerHttpTestHelper.CreateDockerClient((_, _) => Task.FromResult(DockerHttpTestHelper.JsonResponse(new { })));
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var httpFactory = new SimpleHttpClientFactory(new HttpClient(handler));

        var sut = new KeycloakDockerService(client, fakeDockerService, httpFactory, NullLogger<KeycloakDockerService>.Instance);

        var ready = await sut.WaitUntilReadyAsync("http://localhost:8080/auth/realms/modulix", TimeSpan.FromMilliseconds(200));

        Assert.False(ready);
    }

    [Theory]
    [InlineData("running", true)]
    [InlineData("exited", false)]
    [InlineData(null, false)]
    public async Task IsKeycloakRunningAsync_ChecksContainerState(string? state, bool expectedRunning)
    {
        var fakeDockerService = new FakeInternalDockerService();
        var client = DockerHttpTestHelper.CreateDockerClient((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/containers/json", StringComparison.Ordinal))
            {
                if (state == null)
                {
                    return Task.FromResult(DockerHttpTestHelper.JsonResponse(Array.Empty<object>()));
                }

                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new[]
                {
                    new
                    {
                        Names = new[] { "/keycloak" },
                        State = state
                    }
                }));
            }

            return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { }));
        });

        var httpFactory = new SimpleHttpClientFactory(new HttpClient());
        var sut = new KeycloakDockerService(client, fakeDockerService, httpFactory, NullLogger<KeycloakDockerService>.Instance);

        var running = await sut.IsKeycloakRunningAsync();

        Assert.Equal(expectedRunning, running);
    }

    [Fact]
    public async Task RunKeycloakStackAsync_StartsContainersWhenStopped()
    {
        var startedContainers = new List<string>();
        var fakeDockerService = new FakeInternalDockerService();
        var client = DockerHttpTestHelper.CreateDockerClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/containers/json", StringComparison.Ordinal))
            {
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new[]
                {
                    new { ID = "db-id", Names = new[] { "/keycloak-db" }, State = "exited" },
                    new { ID = "kc-id", Names = new[] { "/keycloak" }, State = "exited" }
                }));
            }

            if (request.Method == HttpMethod.Post && path.Contains("/start", StringComparison.Ordinal))
            {
                startedContainers.Add(path);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { }));
        });

        var httpFactory = new SimpleHttpClientFactory(new HttpClient());
        var sut = new KeycloakDockerService(client, fakeDockerService, httpFactory, NullLogger<KeycloakDockerService>.Instance);

        await sut.RunKeycloakStackAsync();

        Assert.Equal(2, startedContainers.Count);
        Assert.Contains("/containers/db-id/start", startedContainers[0]);
        Assert.Contains("/containers/kc-id/start", startedContainers[1]);
    }

    [Fact]
    public async Task StopKeycloakStackAsync_StopsRunningContainers()
    {
        var stoppedContainers = new List<string>();
        var fakeDockerService = new FakeInternalDockerService();
        var client = DockerHttpTestHelper.CreateDockerClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/containers/json", StringComparison.Ordinal))
            {
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new[]
                {
                    new { ID = "db-id", Names = new[] { "/keycloak-db" }, State = "running" },
                    new { ID = "kc-id", Names = new[] { "/keycloak" }, State = "running" }
                }));
            }

            if (request.Method == HttpMethod.Post && path.Contains("/stop", StringComparison.Ordinal))
            {
                stoppedContainers.Add(path);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { }));
        });

        var httpFactory = new SimpleHttpClientFactory(new HttpClient());
        var sut = new KeycloakDockerService(client, fakeDockerService, httpFactory, NullLogger<KeycloakDockerService>.Instance);

        await sut.StopKeycloakStackAsync();

        Assert.Equal(2, stoppedContainers.Count);
        Assert.Contains("/containers/kc-id/stop", stoppedContainers[0]);
        Assert.Contains("/containers/db-id/stop", stoppedContainers[1]);
    }

    [Fact]
    public async Task EnsureKeycloakStackAsync_EnsuresNetworkAndRunsExistingContainers()
    {
        var fakeDockerService = new FakeInternalDockerService();
        var client = DockerHttpTestHelper.CreateDockerClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/containers/json", StringComparison.Ordinal))
            {
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new[]
                {
                    new { ID = "db-id", Names = new[] { "/keycloak-db" }, State = "running" },
                    new { ID = "kc-id", Names = new[] { "/keycloak" }, State = "running" }
                }));
            }

            return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { }));
        });

        var httpFactory = new SimpleHttpClientFactory(new HttpClient());
        var sut = new KeycloakDockerService(client, fakeDockerService, httpFactory, NullLogger<KeycloakDockerService>.Instance);

        await sut.EnsureKeycloakStackAsync("localhost:8080");

        Assert.True(fakeDockerService.EnsureNetworkCalled);
        Assert.Equal("keycloak-net", fakeDockerService.EnsuredNetworkName);
    }

    private sealed class SimpleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name = "") => client;
    }

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }

    private sealed class FakeInternalDockerService : IDockerService
    {
        public bool EnsureNetworkCalled { get; private set; }
        public string? EnsuredNetworkName { get; private set; }

        public Task<string> BuildContainerAsync(string buildContextPath, string dockerfileName, string imageName, string containerName, string networkName, int containerPort, IList<string>? env = null, IList<string>? binds = null, CancellationToken ct = default)
            => Task.FromResult("built-container");

        public Task EnsureNetworkExistsAsync(string networkName, CancellationToken ct = default)
        {
            EnsureNetworkCalled = true;
            EnsuredNetworkName = networkName;
            return Task.CompletedTask;
        }

        public Task RunContainerAsync(string containerId, CancellationToken ct = default) => Task.CompletedTask;
        public Task WaitUntilReadyAsync(string containerId, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopContainerAsync(string containerId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveContainerAsync(string containerId, string containerName, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> TryRemoveModuleContainerAsync(string containerId, string containerName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> IsContainerRunningAsync(string containerId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<ContainerStats> GetContainerStatsAsync(string containerId, CancellationToken ct = default) => Task.FromResult(new ContainerStats());
    }
}
