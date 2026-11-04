using System.Net;

using Docker.DotNet.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Docker.Models;
using Modulix.Docker.Services;
using Modulix.Docker.Services.Interfaces;

namespace Modulix.Docker.Tests.Services;

[Trait("Category", "Services")]
[Trait("SubCategory", "KeycloakDockerService")]
public class KeycloakDockerServiceTests
{
    [Fact]
    public async Task WaitUntilReadyAsync_WhenDiscoveryEndpointReturns200_ReturnsTrue()
    {
        var fakeDockerService = new FakeInternalDockerService();
        var handler = new FakeHttpHandler((request, _) =>
        {
            Assert.Contains("/.well-known/openid-configuration", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var httpFactory = new SimpleHttpClientFactory(new HttpClient(handler));

        var sut = new KeycloakDockerService(fakeDockerService, httpFactory, NullLogger<KeycloakDockerService>.Instance);

        var ready = await sut.WaitUntilReadyAsync("http://localhost:8080/auth/realms/modulix", TimeSpan.FromSeconds(5));

        Assert.True(ready);
    }

    [Fact]
    public async Task WaitUntilReadyAsync_WhenDiscoveryEndpointTimesOut_ReturnsFalse()
    {
        var fakeDockerService = new FakeInternalDockerService();
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var httpFactory = new SimpleHttpClientFactory(new HttpClient(handler));

        var sut = new KeycloakDockerService(fakeDockerService, httpFactory, NullLogger<KeycloakDockerService>.Instance);

        var ready = await sut.WaitUntilReadyAsync("http://localhost:8080/auth/realms/modulix", TimeSpan.FromMilliseconds(200));

        Assert.False(ready);
    }

    [Theory]
    [InlineData("running", true)]
    [InlineData("exited", false)]
    [InlineData(null, false)]
    public async Task IsKeycloakRunningAsync_ChecksContainerState(string? state, bool expectedRunning)
    {
        var fakeDockerService = new FakeInternalDockerService { KeycloakRunning = state == "running" };
        var httpFactory = new SimpleHttpClientFactory(new HttpClient());
        var sut = new KeycloakDockerService(fakeDockerService, httpFactory, NullLogger<KeycloakDockerService>.Instance);

        var running = await sut.IsKeycloakRunningAsync();

        Assert.Equal(expectedRunning, running);
        Assert.Equal(new[] { "keycloak" }, fakeDockerService.RunningContainerLookups);
    }

    [Fact]
    public async Task RunKeycloakStackAsync_StartsContainersWhenStopped()
    {
        var fakeDockerService = new FakeInternalDockerService();
        var httpFactory = new SimpleHttpClientFactory(new HttpClient());
        var sut = new KeycloakDockerService(fakeDockerService, httpFactory, NullLogger<KeycloakDockerService>.Instance);

        await sut.RunKeycloakStackAsync();

        Assert.Equal(new[] { "keycloak-db", "keycloak" }, fakeDockerService.StartedContainerNames);
    }

    [Fact]
    public async Task StopKeycloakStackAsync_StopsRunningContainers()
    {
        var fakeDockerService = new FakeInternalDockerService();
        var httpFactory = new SimpleHttpClientFactory(new HttpClient());
        var sut = new KeycloakDockerService(fakeDockerService, httpFactory, NullLogger<KeycloakDockerService>.Instance);

        await sut.StopKeycloakStackAsync();

        Assert.Equal(new[] { "keycloak", "keycloak-db" }, fakeDockerService.StoppedContainerNames);
    }

    [Fact]
    public async Task EnsureKeycloakStackAsync_DelegatesContainerConfigurationAndBuildContexts()
    {
        var fakeDockerService = new FakeInternalDockerService();
        var httpFactory = new SimpleHttpClientFactory(new HttpClient());
        var sut = new KeycloakDockerService(fakeDockerService, httpFactory, NullLogger<KeycloakDockerService>.Instance);

        await sut.EnsureKeycloakStackAsync("localhost:8080");

        Assert.True(fakeDockerService.EnsureNetworkCalled);
        Assert.Equal("keycloak-net", fakeDockerService.EnsuredNetworkName);
        Assert.Equal(0, fakeDockerService.EnsureImageCalls);
        Assert.Equal(new[] { "keycloak-db", "keycloak" },
            fakeDockerService.EnsuredContainers.Select(container => container.Parameters.Name));

        var resourcesPath = Path.Combine(AppContext.BaseDirectory, "Resources");
        var database = fakeDockerService.EnsuredContainers[0];
        Assert.Equal("modulix-postgres:latest", database.Parameters.Image);
        Assert.Equal(Path.Combine(resourcesPath, "Postgres"), database.BuildContextPath);
        Assert.Equal("Dockerfile", database.DockerfileName);
        Assert.Equal("keycloak-net", database.Parameters.HostConfig!.NetworkMode);
        Assert.Equal(RestartPolicyKind.Always, database.Parameters.HostConfig.RestartPolicy!.Name);
        Assert.Equal(new[] { "postgres-data:/var/lib/postgresql/data:Z" }, database.Parameters.HostConfig.Binds);

        var keycloak = fakeDockerService.EnsuredContainers[1];
        Assert.Equal("modulix-keycloak:latest", keycloak.Parameters.Image);
        Assert.Equal(Path.Combine(resourcesPath, "Keycloak"), keycloak.BuildContextPath);
        Assert.Equal("Dockerfile", keycloak.DockerfileName);
        Assert.Equal("keycloak-net", keycloak.Parameters.HostConfig!.NetworkMode);
        Assert.Equal(RestartPolicyKind.Always, keycloak.Parameters.HostConfig.RestartPolicy!.Name);
        Assert.Equal(new[] { "KC_HOSTNAME=http://localhost:8080" }, keycloak.Parameters.Env);
        Assert.Contains("8080/tcp", keycloak.Parameters.ExposedPorts!.Keys);
        var portBinding = Assert.Single(keycloak.Parameters.HostConfig.PortBindings!["8080/tcp"]);
        Assert.Equal("8080", portBinding.HostPort);
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
        public int EnsureImageCalls { get; private set; }
        public bool KeycloakRunning { get; set; }
        public List<string> RunningContainerLookups { get; } = [];
        public List<ContainerEnsure> EnsuredContainers { get; } = [];
        public List<string> StartedContainerNames { get; } = [];
        public List<string> StoppedContainerNames { get; } = [];

        public Task<string> BuildContainerAsync(string buildContextPath, string dockerfileName, string imageName, string containerName, string networkName, int containerPort, IList<string>? env = null, IList<string>? binds = null, CancellationToken ct = default)
            => Task.FromResult("built-container");

        public Task EnsureNetworkExistsAsync(string networkName, CancellationToken ct = default)
        {
            EnsureNetworkCalled = true;
            EnsuredNetworkName = networkName;
            return Task.CompletedTask;
        }

        public Task EnsureImageExistsAsync(string imageTag, string buildContextPath, string dockerfileName = "Dockerfile", CancellationToken ct = default)
        {
            EnsureImageCalls++;
            return Task.CompletedTask;
        }

        public Task EnsureContainerRunningAsync(
            CreateContainerParameters parameters,
            string buildContextPath,
            string dockerfileName = "Dockerfile",
            CancellationToken ct = default)
        {
            EnsuredContainers.Add(new ContainerEnsure(parameters, buildContextPath, dockerfileName));
            return Task.CompletedTask;
        }

        public Task<bool> IsContainerRunningByNameAsync(string containerName, CancellationToken ct = default)
        {
            RunningContainerLookups.Add(containerName);
            return Task.FromResult(KeycloakRunning);
        }

        public Task StartContainerByNameAsync(string containerName, CancellationToken ct = default)
        {
            StartedContainerNames.Add(containerName);
            return Task.CompletedTask;
        }

        public Task StopContainerByNameAsync(string containerName, CancellationToken ct = default)
        {
            StoppedContainerNames.Add(containerName);
            return Task.CompletedTask;
        }

        public Task RunContainerAsync(string containerId, CancellationToken ct = default) => Task.CompletedTask;
        public Task WaitUntilReadyAsync(string containerId, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopContainerAsync(string containerId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveContainerAsync(string containerId, string containerName, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> TryRemoveContainerAsync(string containerId, string containerName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> IsContainerRunningAsync(string containerId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<ContainerStats> GetContainerStatsAsync(string containerId, CancellationToken ct = default) => Task.FromResult(new ContainerStats());

        public sealed record ContainerEnsure(
            CreateContainerParameters Parameters,
            string BuildContextPath,
            string DockerfileName);
    }
}
