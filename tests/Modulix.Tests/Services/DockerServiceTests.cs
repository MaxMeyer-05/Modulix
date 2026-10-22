using System.Net;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Modulix.Docker.Options;
using Modulix.Docker.Services;
using Modulix.Tests.TestData;

namespace Modulix.Tests.Services;

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

    #endregion

    #region GetContainerLogs Tests

    [Theory]
    [InlineData("stdout line\n", "", null, "all", "stdout line\n")]
    [InlineData("", "stderr line\n", 100, "100", "stderr line\n")]
    [InlineData("stdout line\n", "stderr error\n", 250, "250", "stdout line\r\nstderr error\n")]
    [InlineData("", "", null, "all", "")]
    public async Task GetContainerLogsAsync_MultiplexedOutput_CombinesStdoutAndStderr(
        string stdout, string stderr, int? tail, string expectedTailQuery, string expectedOutput)
    {
        using var service = CreateService((request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.EndsWith("/containers/log-container/logs", request.RequestUri!.AbsolutePath);
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            Assert.Equal("1", query["stdout"].ToString());
            Assert.Equal("1", query["stderr"].ToString());
            Assert.Equal("1", query["timestamps"].ToString());
            Assert.Equal(expectedTailQuery, query["tail"].ToString());

            return Task.FromResult(DockerHttpTestHelper.LogResponse(stdout, stderr));
        });

        var actual = await service.GetContainerLogsAsync("log-container", tail);

        Assert.Equal(expectedOutput.Replace("\r\n", Environment.NewLine), actual);
    }

    [Fact]
    public async Task GetContainerLogsAsync_MissingContainer_ThrowsKeyNotFoundException()
    {
        using var service = CreateService((request, cancellationToken) =>
            Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "No such container." }, HttpStatusCode.NotFound)));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetContainerLogsAsync("missing-container"));
    }

    #endregion

    private static DockerService CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder,
        DockerOptions? options = null,
        Func<int, CancellationToken, Task<bool>>? portProbe = null)
    {
        var client = DockerHttpTestHelper.CreateDockerClient(responder);
        return new DockerService(NullLogger<DockerService>.Instance, Options.Create(options ?? new DockerOptions()), client, portProbe);
    }
}