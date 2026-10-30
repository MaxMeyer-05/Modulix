using System.Net;

using Docker.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Modulix.Docker.Extensions;
using Modulix.Docker.Services;
using Modulix.Docker.Services.Interfaces;
using Modulix.Docker.Tests.TestData;

namespace Modulix.Docker.Tests.Services;

/// <summary>
/// Readiness, runtime status inspection, and log retrieval tests for <see cref="DockerService"/>.
/// </summary>
[Trait("Category", "Services")]
[Trait("SubCategory", "DockerService")]
public partial class DockerServiceTests
{
    #region WaitUntilReady & IsContainerRunning Tests

    [Theory]
    [InlineData("healthy", true, false, true)]
    [InlineData("unhealthy", true, false, false)]
    [InlineData(null, true, false, true)]
    [InlineData("", true, false, true)]
    [InlineData("none", true, false, true)]
    [InlineData("healthy", false, false, false)]
    [InlineData("healthy", true, true, false)]
    public async Task WaitUntilReadyAsync_ContainerState_RequiresRunningHealthyContainer(
        string? health, bool running, bool restarting, bool expectedReady)
    {
        using var service = CreateService((request, cancellationToken) =>
        {
            Assert.EndsWith("/containers/candidate/json", request.RequestUri!.AbsolutePath);
            return Task.FromResult(DockerHttpTestHelper.JsonResponse(new
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
        using var service = CreateService((request, cancellationToken) => Task.FromResult(DockerHttpTestHelper.JsonResponse(new
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

    [Fact]
    public async Task WaitUntilReadyAsync_WithoutHealthcheckAndWithBoundPort_WaitsForPortAndVerifiesContainerStillRunning()
    {
        var inspections = 0;
        var portChecks = 0;
        using var service = CreateService(
            (request, cancellationToken) =>
            {
                inspections++;
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new
                {
                    State = new { Running = true, Restarting = false, ExitCode = 0 },
                    HostConfig = new
                    {
                        PortBindings = new Dictionary<string, object[]>
                        {
                            ["8080/tcp"] = [new { HostIp = "127.0.0.1", HostPort = "8080" }]
                        }
                    }
                }));
            },
            portProbe: (port, ct) =>
            {
                Assert.Equal(8080, port);
                return Task.FromResult(++portChecks >= 2);
            });

        await service.WaitUntilReadyAsync("candidate");

        Assert.Equal(2, portChecks);
        Assert.Equal(3, inspections);
    }

    [Fact]
    public async Task WaitUntilReadyAsync_ContainerCrashesBeforePortAcceptsConnections_ThrowsInvalidOperationException()
    {
        var inspections = 0;
        using var service = CreateService(
            (request, cancellationToken) =>
            {
                inspections++;
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new
                {
                    State = new
                    {
                        Running = inspections == 1,
                        Restarting = false,
                        ExitCode = inspections == 1 ? 0 : 139
                    },
                    HostConfig = new
                    {
                        PortBindings = new Dictionary<string, object[]>
                        {
                            ["8080/tcp"] = [new { HostIp = "127.0.0.1", HostPort = "8080" }]
                        }
                    }
                }));
            },
            portProbe: (_, _) => Task.FromResult(false));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.WaitUntilReadyAsync("candidate"));

        Assert.Contains("ExitCode: 139", exception.Message);
        Assert.Equal(2, inspections);
    }

    [Theory]
    [InlineData(true, false, 0, null, HttpStatusCode.OK, true)]
    [InlineData(true, false, 0, "healthy", HttpStatusCode.OK, true)]
    [InlineData(true, false, 0, "unhealthy", HttpStatusCode.OK, false)]
    [InlineData(false, false, 0, null, HttpStatusCode.OK, false)]
    [InlineData(true, true, 139, null, HttpStatusCode.OK, false)]
    [InlineData(true, false, 1, null, HttpStatusCode.OK, false)]
    [InlineData(false, false, 0, null, HttpStatusCode.NotFound, false)]
    public async Task IsContainerRunningAsync_InspectsContainerState_ReturnsExpectedResult(
        bool running, bool restarting, int exitCode, string? health, HttpStatusCode statusCode, bool expectedRunning)
    {
        using var service = CreateService((request, cancellationToken) =>
        {
            if (statusCode == HttpStatusCode.NotFound)
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "No such container." }, HttpStatusCode.NotFound));

            return Task.FromResult(DockerHttpTestHelper.JsonResponse(new
            {
                State = new
                {
                    Running = running,
                    Restarting = restarting,
                    ExitCode = exitCode,
                    Health = health is null ? null : new { Status = health }
                }
            }));
        });

        var actual = await service.IsContainerRunningAsync("candidate");

        Assert.Equal(expectedRunning, actual);
    }

    [Fact]
    public async Task GetContainerStatsAsync_RunningContainer_CalculatesCpuMemoryAndUptime()
    {
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-90).ToString("O");
        using var service = CreateService((request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/containers/candidate/json", StringComparison.Ordinal))
            {
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new
                {
                    State = new
                    {
                        Running = true,
                        Restarting = false,
                        ExitCode = 0,
                        StartedAt = startedAt
                    }
                }));
            }

            Assert.EndsWith("/containers/candidate/stats", request.RequestUri.AbsolutePath);
            Assert.Contains("stream=0", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);

            return Task.FromResult(DockerHttpTestHelper.JsonResponse(new
            {
                memory_stats = new
                {
                    usage = 100UL * 1024 * 1024,
                    limit = 512UL * 1024 * 1024,
                    stats = new Dictionary<string, ulong>
                    {
                        ["inactive_file"] = 20UL * 1024 * 1024
                    }
                },
                cpu_stats = new
                {
                    cpu_usage = new { total_usage = 250_000_000UL },
                    system_cpu_usage = 2_000_000_000UL,
                    online_cpus = 2U
                },
                precpu_stats = new
                {
                    cpu_usage = new { total_usage = 150_000_000UL },
                    system_cpu_usage = 1_000_000_000UL
                }
            }));
        });

        var stats = await service.GetContainerStatsAsync("candidate");

        Assert.True(stats.IsRunning);
        Assert.Equal(20.0, stats.CpuUsagePercent);
        Assert.Equal(80.0, stats.MemoryUsageMiB);
        Assert.Equal(512.0, stats.MemoryLimitMiB);
        Assert.True(stats.UptimeSeconds >= 85.0);
        Assert.Equal(0, stats.ExitCode);
    }

    [Fact]
    public async Task GetContainerStatsAsync_StoppedContainer_ReturnsStoppedStateAndExitCodeWithoutQueryingLiveStats()
    {
        var statsRequested = false;
        using var service = CreateService((request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/stats", StringComparison.Ordinal))
            {
                statsRequested = true;
            }

            return Task.FromResult(DockerHttpTestHelper.JsonResponse(new
            {
                State = new
                {
                    Running = false,
                    Restarting = false,
                    ExitCode = 137
                }
            }));
        });

        var stats = await service.GetContainerStatsAsync("candidate");

        Assert.False(stats.IsRunning);
        Assert.Equal(137, stats.ExitCode);
        Assert.Equal(0.0, stats.CpuUsagePercent);
        Assert.Equal(0.0, stats.MemoryUsageMiB);
        Assert.False(statsRequested);
    }

    [Fact]
    public async Task GetContainerStatsAsync_MissingContainer_ReturnsDefaultStats()
    {
        using var service = CreateService((_, _) =>
            Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "No such container." }, HttpStatusCode.NotFound)));

        var stats = await service.GetContainerStatsAsync("missing-container");

        Assert.False(stats.IsRunning);
        Assert.Equal(0, stats.ExitCode);
    }

    #endregion

    #region DockerRegistration Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddDockerServices_WithoutSocketUri_UsesPlatformDefaultEndpoint(string? socketUri)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (socketUri is not null)
            services.Configure<DockerSettings>(options => options.SocketUri = socketUri);

        services.AddDockerServices();

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<DockerClient>();
        var expectedUri = OperatingSystem.IsWindows()
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");

        Assert.Equal(expectedUri, client.Configuration.EndpointBaseUri);
    }

    [Theory]
    [InlineData("unix:///run/user/1000/podman/podman.sock")]
    [InlineData("  tcp://127.0.0.1:2375  ")]
    public void AddDockerServices_WithCustomSocketUri_UsesConfiguredEndpoint(string configuredSocketUri)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<DockerSettings>(options => options.SocketUri = configuredSocketUri);
        services.AddDockerServices();

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<DockerClient>();

        Assert.Equal(new Uri(configuredSocketUri.Trim()), client.Configuration.EndpointBaseUri);
    }

    [Fact]
    public void AddDockerServices_RegistersAllExpectedServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDockerServices();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<DockerClient>());
        Assert.NotNull(provider.GetService<DockerExtension>());
        Assert.NotNull(provider.GetService<IDockerService>());
        Assert.NotNull(provider.GetService<IModuleDockerService>());
        Assert.NotNull(provider.GetService<IKeycloakDockerService>());
    }

    #endregion

    private static DockerService CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder,
        Func<int, CancellationToken, Task<bool>>? portProbe = null)
    {
        var client = DockerHttpTestHelper.CreateDockerClient(responder);
        var dockerExtension = new DockerExtension(NullLogger<DockerExtension>.Instance);
        return new DockerService(NullLogger<DockerService>.Instance, client, dockerExtension, portProbe);
    }
}
