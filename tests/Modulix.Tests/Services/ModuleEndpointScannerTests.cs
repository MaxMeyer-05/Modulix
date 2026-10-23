using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

using Docker.DotNet;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Modulix.Client.Models.Dtos;
using Modulix.Database.DbContexts;
using Modulix.Docker.Options;
using Modulix.Scanner;
using Modulix.Services.Interfaces;
using Modulix.Tests.TestData;

namespace Modulix.Tests.Services;

[Trait("Category", "Services")]
[Trait("SubCategory", "ModuleEndpointScanner")]
public partial class ModuleEndpointScannerTests : IDisposable
{
    private const string ValidOutput = "{\"EntryAssemblyFileName\":\"module.dll\",\"DiscoveredEndpoints\":[{\"HttpMethod\":{\"Method\":\"GET\"},\"EndpointPath\":\"/health\"}]}";
    private readonly string _testRootDirectory = Path.Combine(Path.GetTempPath(), "Modulix_ServerScanner_Tests_" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly ConcurrentQueue<JsonElement> _createdContainers = new();
    private readonly RecordingModuleService _moduleService = new();
    private readonly SqliteConnection _connection;
    private readonly ServerContext _context;
    private readonly ServiceProvider _serviceProvider;
    private readonly DockerClient _client;
    private readonly ModuleEndpointScanner _sut;
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
    private int _containerCount;

    #region Setup & Teardown

    public ModuleEndpointScannerTests()
    {
        Directory.CreateDirectory(_testRootDirectory);
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new ServerContext(new DbContextOptionsBuilder<ServerContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _serviceProvider = new ServiceCollection()
            .AddSingleton(_context)
            .AddSingleton<IScannerService>(_moduleService)
            .BuildServiceProvider();
        _responder = RespondAsync;
        _client = DockerHttpTestHelper.CreateDockerClient((request, cancellationToken) =>
        {
            _requests.Enqueue(request.Method + " " + request.RequestUri!.AbsolutePath);
            return _responder(request, cancellationToken);
        });
        _sut = new ModuleEndpointScanner(_client, NullLogger<ModuleEndpointScanner>.Instance,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DockerOptions { MaxConcurrentScans = 1, ScannerImage = "custom-scanner:test" }));
    }

    public void Dispose()
    {
        _sut.Dispose();
        _serviceProvider.Dispose();
        _client.Dispose();
        _context.Dispose();
        _connection.Dispose();
        Directory.Delete(_testRootDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Scan Validation Tests

    [Fact]
    public async Task ScanOrEnqueueAsync_MissingDirectory_ThrowsBeforeCallingDocker()
    {
        var missingDirectory = Path.Combine(_testRootDirectory, "missing");

        var exception = await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            _sut.ScanOrEnqueueAsync(Guid.NewGuid(), missingDirectory));

        Assert.Contains(missingDirectory, exception.Message);
        Assert.Empty(_requests);
    }

    [Fact]
    public void CancelScan_UnknownModule_ReturnsFalseWithoutCallingDocker()
    {
        Assert.False(_sut.CancelScan(Guid.NewGuid()));
        Assert.Empty(_requests);
    }

    #endregion

    #region Immediate Scan Tests

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScanOrEnqueueAsync_AvailableSlot_ReturnsEndpointsAndUsesIsolatedReadOnlyContainer(bool camelCase)
    {
        _responder = (request, cancellationToken) => request.RequestUri!.AbsolutePath.EndsWith("/logs", StringComparison.Ordinal)
            ? Task.FromResult(DockerHttpTestHelper.LogResponse(camelCase
                ? "{\"entryAssemblyFileName\":\"module.dll\",\"discoveredEndpoints\":[{\"httpMethod\":{\"method\":\"GET\"},\"endpointPath\":\"/health\"}]}"
                : ValidOutput, "diagnostic output"))
            : RespondAsync(request, cancellationToken);

        var response = await _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory);

        Assert.False(response.WasQueued);
        Assert.NotNull(response.Result);
        Assert.Equal("module.dll", response.Result.EntryAssemblyFileName);
        var endpoint = Assert.Single(response.Result.DiscoveredEndpoints);
        Assert.Equal(HttpMethod.Get, endpoint.HttpMethod);
        Assert.Equal("/health", endpoint.EndpointPath);
        Assert.False(_moduleService.Results.Reader.TryRead(out _));
        var container = Assert.Single(_createdContainers);
        Assert.Equal("custom-scanner:test", container.GetProperty("Image").GetString());
        Assert.True(container.GetProperty("NetworkDisabled").GetBoolean());
        Assert.Equal("/scan-target", Assert.Single(container.GetProperty("Cmd").EnumerateArray()).GetString());
        var hostConfig = container.GetProperty("HostConfig");
        Assert.Equal($"{_testRootDirectory}:/scan-target:ro", Assert.Single(hostConfig.GetProperty("Binds").EnumerateArray()).GetString());
        Assert.Equal("ALL", Assert.Single(hostConfig.GetProperty("CapDrop").EnumerateArray()).GetString());
        Assert.False(hostConfig.TryGetProperty("AutoRemove", out var autoRemove) && autoRemove.GetBoolean());
        Assert.EndsWith("/containers/scan-1", _requests.Last());
        Assert.StartsWith("DELETE ", _requests.Last());
    }

    [Fact]
    public async Task ScanOrEnqueueAsync_ImageNotFoundLocally_PullsScannerImageAndRetriesCreate()
    {
        var createAttempts = 0;
        string? imageQuery = null;
        _responder = (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/containers/create", StringComparison.Ordinal) && ++createAttempts == 1)
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "No such image: custom-scanner:test" }, HttpStatusCode.NotFound));
            if (path.EndsWith("/images/create", StringComparison.Ordinal))
            {
                imageQuery = request.RequestUri.Query;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"Pull complete\"}\n", Encoding.UTF8, "application/json")
                });
            }
            return RespondAsync(request, cancellationToken);
        };

        var response = await _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory);

        Assert.False(response.WasQueued);
        Assert.NotNull(response.Result);
        Assert.Equal(2, createAttempts);
        Assert.NotNull(imageQuery);
        Assert.Contains("fromImage=custom-scanner", imageQuery, StringComparison.Ordinal);
        Assert.Contains("tag=test", imageQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScanOrEnqueueAsync_NonzeroExit_ReportsStderrAndRemovesContainer()
    {
        _responder = (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/wait", StringComparison.Ordinal))
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { StatusCode = 17 }));
            if (path.EndsWith("/logs", StringComparison.Ordinal))
                return Task.FromResult(DockerHttpTestHelper.LogResponse(ValidOutput, "Dependency could not be loaded."));
            return RespondAsync(request, cancellationToken);
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory));

        Assert.Contains("17", exception.Message);
        Assert.Contains("Dependency could not be loaded.", exception.Message);
        Assert.EndsWith("/containers/scan-1", _requests.Last());
        Assert.StartsWith("DELETE ", _requests.Last());
    }

    [Theory]
    [InlineData("null", typeof(InvalidOperationException))]
    [InlineData("{}", typeof(InvalidOperationException))]
    [InlineData("{\"EntryAssemblyFileName\":\" \"}", typeof(InvalidOperationException))]
    [InlineData("not json", typeof(JsonException))]
    public async Task ScanOrEnqueueAsync_InvalidResult_RejectsOutputAndRemovesContainer(string output, Type exceptionType)
    {
        _responder = (request, cancellationToken) => request.RequestUri!.AbsolutePath.EndsWith("/logs", StringComparison.Ordinal)
            ? Task.FromResult(DockerHttpTestHelper.LogResponse(output))
            : RespondAsync(request, cancellationToken);

        await Assert.ThrowsAsync(exceptionType, () => _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory));

        Assert.StartsWith("DELETE ", _requests.Last());
        Assert.EndsWith("/containers/scan-1", _requests.Last());
    }

    [Theory]
    [InlineData("/containers/create", false)]
    [InlineData("/start", true)]
    [InlineData("/wait", true)]
    [InlineData("/logs", true)]
    public async Task ScanOrEnqueueAsync_DockerFailure_RemovesOnlyCreatedContainersAndReleasesSlot(string failingPath, bool containerCreated)
    {
        _responder = (request, cancellationToken) => request.RequestUri!.AbsolutePath.EndsWith(failingPath, StringComparison.Ordinal)
            ? Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "Docker unavailable." }, HttpStatusCode.InternalServerError))
            : RespondAsync(request, cancellationToken);

        var exception = await Assert.ThrowsAsync<DockerApiException>(() =>
            _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory));

        Assert.Contains("Docker unavailable.", exception.Message);
        Assert.Equal(containerCreated ? 1 : 0, _requests.Count(request => request.StartsWith("DELETE ", StringComparison.Ordinal)));
        _responder = RespondAsync;
        var retry = await _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory);
        Assert.False(retry.WasQueued);
        Assert.NotNull(retry.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScanOrEnqueueAsync_CleanupFails_PreservesScanResultOrOriginalFailure(bool scanFails)
    {
        _responder = (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Delete)
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "Cleanup failed." }, HttpStatusCode.InternalServerError));
            if (scanFails && request.RequestUri!.AbsolutePath.EndsWith("/wait", StringComparison.Ordinal))
                return Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "Original scan failure." }, HttpStatusCode.InternalServerError));
            return RespondAsync(request, cancellationToken);
        };

        if (scanFails)
        {
            var exception = await Assert.ThrowsAsync<DockerApiException>(() =>
                _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory));
            Assert.Contains("Original scan failure.", exception.Message);
        }
        else
        {
            var response = await _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory);
            Assert.Equal("module.dll", response.Result!.EntryAssemblyFileName);
        }
        Assert.Single(_requests, request => request.StartsWith("DELETE ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanOrEnqueueAsync_CallerCancelsRunningScan_CleansWithIndependentTokenAndReleasesSlot()
    {
        using var cancellation = new CancellationTokenSource();
        _responder = (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/wait", StringComparison.Ordinal))
            {
                cancellation.Cancel();
                return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
            }
            if (request.Method == HttpMethod.Delete)
            {
                Assert.False(cancellationToken.IsCancellationRequested);
                Assert.Contains("force=true", request.RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
            }
            return RespondAsync(request, cancellationToken);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory, ct: cancellation.Token));

        Assert.StartsWith("DELETE ", _requests.Last());
        _responder = RespondAsync;
        Assert.False((await _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory)).WasQueued);
    }

    #endregion

    #region Test Fakes & Helpers

    private void BlockFirstScan(TaskCompletionSource started, TaskCompletionSource release)
    {
        _responder = async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/containers/scan-1/wait", StringComparison.Ordinal))
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
            return await RespondAsync(request, cancellationToken);
        };
    }

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/containers/create", StringComparison.Ordinal))
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            _createdContainers.Enqueue(body.RootElement.Clone());
            return DockerHttpTestHelper.JsonResponse(new { Id = "scan-" + Interlocked.Increment(ref _containerCount) });
        }
        if (path.EndsWith("/start", StringComparison.Ordinal) || request.Method == HttpMethod.Delete)
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        if (path.EndsWith("/wait", StringComparison.Ordinal))
            return DockerHttpTestHelper.JsonResponse(new { StatusCode = 0 });
        if (path.EndsWith("/logs", StringComparison.Ordinal))
            return DockerHttpTestHelper.LogResponse(ValidOutput);
        throw new InvalidOperationException("Unexpected Docker request: " + request.RequestUri);
    }

    private sealed class RecordingModuleService : IScannerService
    {
        public Channel<(Guid ModuleId, ModuleScanResultDto Result)> Results { get; } =
            Channel.CreateUnbounded<(Guid, ModuleScanResultDto)>();

        public Task ProcessQueuedScanResultAsync(Guid moduleId, ModuleScanResultDto result, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Results.Writer.TryWrite((moduleId, result));
            return Task.CompletedTask;
        }

        public Task<IEnumerable<ModuleScanQueueItemDto>> GetScanQueueAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    #endregion
}