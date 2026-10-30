using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Modulix.Docker.Services.Interfaces;
using Modulix.Services;

namespace Modulix.Tests.Services;

[Trait("Category", "Services")]
[Trait("SubCategory", "KeycloakService")]
public class KeycloakServiceTests
{
    [Fact]
    public async Task EnsureKeycloakStackAsync_WhenAutoProvisionContainerIsFalse_SetsIsReadyTrueAndSkipsDocker()
    {
        var fakeDocker = new FakeKeycloakDockerService();
        var options = Options.Create(new AuthorizationSettings
        {
            AutoProvisionContainer = false,
            Authority = "http://localhost:8080/realms/modulix"
        });

        var sut = new KeycloakService(fakeDocker, options, NullLogger<KeycloakService>.Instance);

        await sut.EnsureKeycloakStackAsync();

        Assert.True(sut.IsReady);
        Assert.Null(sut.FailureReason);
        Assert.False(fakeDocker.EnsureStackCalled);
    }

    [Fact]
    public async Task EnsureKeycloakStackAsync_WhenAutoProvisionContainerIsTrue_AndReadinessSucceeds_SetsIsReadyTrue()
    {
        var fakeDocker = new FakeKeycloakDockerService { ReadyResult = true };
        var options = Options.Create(new AuthorizationSettings
        {
            AutoProvisionContainer = true,
            Authority = "http://localhost:8080/realms/modulix"
        });

        var sut = new KeycloakService(fakeDocker, options, NullLogger<KeycloakService>.Instance);

        await sut.EnsureKeycloakStackAsync();

        Assert.True(sut.IsReady);
        Assert.Null(sut.FailureReason);
        Assert.True(fakeDocker.EnsureStackCalled);
        Assert.Equal("localhost:8080", fakeDocker.EnsuredAuthorityHost);
    }

    [Fact]
    public async Task EnsureKeycloakStackAsync_WhenReadinessTimesOut_SetsFailureReason()
    {
        var fakeDocker = new FakeKeycloakDockerService { ReadyResult = false };
        var options = Options.Create(new AuthorizationSettings
        {
            AutoProvisionContainer = true,
            Authority = "http://localhost:8080/realms/modulix"
        });

        var sut = new KeycloakService(fakeDocker, options, NullLogger<KeycloakService>.Instance);

        await sut.EnsureKeycloakStackAsync();

        Assert.False(sut.IsReady);
        Assert.Contains("Timed out", sut.FailureReason);
    }

    [Fact]
    public async Task EnsureKeycloakStackAsync_WhenExceptionOccurs_SetsFailureReason()
    {
        var fakeDocker = new FakeKeycloakDockerService
        {
            EnsureStackException = new InvalidOperationException("Docker daemon down")
        };
        var options = Options.Create(new AuthorizationSettings
        {
            AutoProvisionContainer = true,
            Authority = "http://localhost:8080/realms/modulix"
        });

        var sut = new KeycloakService(fakeDocker, options, NullLogger<KeycloakService>.Instance);

        await sut.EnsureKeycloakStackAsync();

        Assert.False(sut.IsReady);
        Assert.Equal("Docker daemon down", sut.FailureReason);
    }

    private sealed class FakeKeycloakDockerService : IKeycloakDockerService
    {
        public bool EnsureStackCalled { get; private set; }
        public string? EnsuredAuthorityHost { get; private set; }
        public bool ReadyResult { get; set; } = true;
        public Exception? EnsureStackException { get; set; }

        public Task EnsureKeycloakStackAsync(string authorityHost, CancellationToken ct = default)
        {
            EnsureStackCalled = true;
            EnsuredAuthorityHost = authorityHost;
            if (EnsureStackException != null)
                throw EnsureStackException;
            return Task.CompletedTask;
        }

        public Task<bool> WaitUntilReadyAsync(string authorityUrl, TimeSpan timeout, CancellationToken ct = default)
            => Task.FromResult(ReadyResult);

        public Task RunKeycloakStackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopKeycloakStackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsKeycloakRunningAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}
