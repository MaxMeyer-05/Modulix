using System.Net;
using System.Buffers.Binary;
using System.Threading.Channels;
using System.Collections.Concurrent;

using System.Text;
using System.Text.Json;

using Docker.DotNet;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Database.DbContexts;
using Modulix.Database.Entities;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

using Modulix.Models.Enums;
using Modulix.Models.Options;

using Modulix.Services;
using Modulix.Services.Interfaces;

namespace Modulix.Tests.Services;

[Trait("Category", "Services")]
[Trait("SubCategory", "ModuleEndpointScanner")]
public class ModuleEndpointScannerTests : IDisposable
{
    private const string ValidOutput = "{\"EntryAssemblyFileName\":\"module.dll\",\"DiscoveredEndpoints\":[{\"HttpMethod\":\"GET\",\"EndpointPath\":\"/health\"}]}";
    private readonly string _testRootDirectory = Path.Combine(Path.GetTempPath(), "Modulix_ServerScanner_Tests_" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly ConcurrentQueue<JsonElement> _createdContainers = new();
    private readonly RecordingModuleService _moduleService = new();
    private readonly SqliteConnection _connection;
    private readonly ServerContext _context;
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
        _responder = RespondAsync;
        _client = new DockerClientConfiguration(new Uri("http://docker.test"),
            new FakeCredentials(new FakeDockerHandler((request, cancellationToken) =>
            {
                _requests.Enqueue(request.Method + " " + request.RequestUri!.AbsolutePath);
                return _responder(request, cancellationToken);
            }))).CreateClient();
        _sut = new ModuleEndpointScanner(_client, NullLogger<ModuleEndpointScanner>.Instance, _context,
            _moduleService, Options.Create(new ModulixOptions { MaxConcurrentScans = 1, ScannerImage = "custom-scanner:test" }));
    }

    public void Dispose()
    {
        _sut.Dispose();
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
            ? Task.FromResult(LogResponse(camelCase
                ? "{\"entryAssemblyFileName\":\"module.dll\",\"discoveredEndpoints\":[{\"httpMethod\":\"GET\",\"endpointPath\":\"/health\"}]}"
                : ValidOutput, "diagnostic output"))
            : RespondAsync(request, cancellationToken);

        var response = await _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory);

        Assert.False(response.WasQueued);
        Assert.NotNull(response.Result);
        Assert.Equal("module.dll", response.Result.EntryAssemblyFileName);
        var endpoint = Assert.Single(response.Result.DiscoveredEndpoints);
        Assert.Equal("GET", endpoint.HttpMethod);
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
    public async Task ScanOrEnqueueAsync_NonzeroExit_ReportsStderrAndRemovesContainer()
    {
        _responder = (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/wait", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse(new { StatusCode = 17 }));
            if (path.EndsWith("/logs", StringComparison.Ordinal))
                return Task.FromResult(LogResponse(ValidOutput, "Dependency could not be loaded."));
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
            ? Task.FromResult(LogResponse(output))
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
            ? Task.FromResult(JsonResponse(new { message = "Docker unavailable." }, HttpStatusCode.InternalServerError))
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
                return Task.FromResult(JsonResponse(new { message = "Cleanup failed." }, HttpStatusCode.InternalServerError));
            if (scanFails && request.RequestUri!.AbsolutePath.EndsWith("/wait", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse(new { message = "Original scan failure." }, HttpStatusCode.InternalServerError));
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

    #region Queue Tests

    [Fact]
    public async Task ScanOrEnqueueAsync_AllSlotsBusy_QueuesWithoutStartingAnotherContainerAndAllowsCancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockFirstScan(started, release);
        var runningScan = _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory);
        var queuedModuleId = Guid.NewGuid();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var queued = await _sut.ScanOrEnqueueAsync(queuedModuleId, _testRootDirectory);

            Assert.True(queued.WasQueued);
            Assert.Null(queued.Result);
            Assert.Single(_createdContainers);
            Assert.True(_sut.CancelScan(queuedModuleId));
            Assert.False(_sut.CancelScan(queuedModuleId));
        }
        finally
        {
            _sut.CancelScan(queuedModuleId);
            release.TrySetResult();
            await runningScan.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(_moduleService.Results.Reader.TryRead(out _));
    }

    [Fact]
    public async Task ScanOrEnqueueAsync_CancelledCallerWithBusySlot_DoesNotQueueWork()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockFirstScan(started, release);
        var runningScan = _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory);
        var queuedModuleId = Guid.NewGuid();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.ScanOrEnqueueAsync(
                queuedModuleId, _testRootDirectory, ct: new CancellationToken(canceled: true)));

            Assert.False(_sut.CancelScan(queuedModuleId));
            Assert.Single(_createdContainers);
        }
        finally
        {
            _sut.CancelScan(queuedModuleId);
            release.TrySetResult();
            await runningScan.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ScanOrEnqueueAsync_QueuedPatchAndCreate_ProcessesPatchFirstAndSkipsCancelledJob()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockFirstScan(started, release);
        var runningScan = _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory);
        var createModuleId = Guid.NewGuid();
        var patchModuleId = Guid.NewGuid();
        var cancelledModuleId = Guid.NewGuid();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True((await _sut.ScanOrEnqueueAsync(createModuleId, _testRootDirectory, ScanPriority.Create)).WasQueued);
            Assert.True((await _sut.ScanOrEnqueueAsync(cancelledModuleId, _testRootDirectory, ScanPriority.Patch)).WasQueued);
            Assert.True((await _sut.ScanOrEnqueueAsync(patchModuleId, _testRootDirectory, ScanPriority.Patch)).WasQueued);
            Assert.True(_sut.CancelScan(cancelledModuleId));

            release.TrySetResult();
            await runningScan.WaitAsync(TimeSpan.FromSeconds(5));
            var patch = await _moduleService.Results.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var create = await _moduleService.Results.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(patchModuleId, patch.ModuleId);
            Assert.Equal(createModuleId, create.ModuleId);
            Assert.Equal("module.dll", patch.Result.EntryAssemblyFileName);
            Assert.Equal("/health", Assert.Single(create.Result.DiscoveredEndpoints).EndpointPath);
            Assert.Equal(3, _createdContainers.Count);
            Assert.False(_moduleService.Results.Reader.TryRead(out _));
        }
        finally
        {
            _sut.CancelScan(createModuleId);
            _sut.CancelScan(patchModuleId);
            _sut.CancelScan(cancelledModuleId);
            release.TrySetResult();
            await runningScan.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    #endregion

    #region Background Scan Tests

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScanOrEnqueueAsync_BackgroundScanFails_MarksExistingModuleFailedAndContinuesQueue(bool moduleExists)
    {
        var failedModuleId = Guid.NewGuid();
        var healthyModuleId = Guid.NewGuid();
        if (moduleExists)
        {
            _context.Modules.Add(new Module
            {
                Id = failedModuleId,
                ModuleName = "Queued module",
                BaseEndpointPath = "api/queued",
                ContainerPort = 8080,
                StoragePath = _testRootDirectory,
                ModuleEntryAssemblyFileName = "module.dll",
                Status = ModuleStatus.QueuedForScan
            });
            await _context.SaveChangesAsync();
        }
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockFirstScan(started, release);
        var originalResponder = _responder;
        _responder = (request, cancellationToken) => request.RequestUri!.AbsolutePath.EndsWith("/containers/scan-2/wait", StringComparison.Ordinal)
            ? Task.FromResult(JsonResponse(new { message = "Background scan failed." }, HttpStatusCode.InternalServerError))
            : originalResponder(request, cancellationToken);
        var runningScan = _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True((await _sut.ScanOrEnqueueAsync(failedModuleId, _testRootDirectory, ScanPriority.Patch)).WasQueued);
            Assert.True((await _sut.ScanOrEnqueueAsync(healthyModuleId, _testRootDirectory, ScanPriority.Create)).WasQueued);
            release.TrySetResult();
            await runningScan.WaitAsync(TimeSpan.FromSeconds(5));

            var processed = await _moduleService.Results.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(healthyModuleId, processed.ModuleId);
            Assert.Equal("module.dll", processed.Result.EntryAssemblyFileName);
            Assert.False(_moduleService.Results.Reader.TryRead(out _));
            Assert.Equal(3, _createdContainers.Count);
            Assert.Equal(3, _requests.Count(request => request.StartsWith("DELETE ", StringComparison.Ordinal)));
            var persisted = await _context.Modules.AsNoTracking().SingleOrDefaultAsync(module => module.Id == failedModuleId);
            if (moduleExists)
            {
                Assert.NotNull(persisted);
                Assert.Equal(ModuleStatus.Failed, persisted.Status);
            }
            else
                Assert.Null(persisted);
        }
        finally
        {
            _sut.CancelScan(failedModuleId);
            _sut.CancelScan(healthyModuleId);
            release.TrySetResult();
            await runningScan.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelScan_QueuedRunningScan_CancelsDockerAndAllowsNextJobToFinish(bool callerCancels)
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedScanStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedScanRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockFirstScan(started, release);
        var originalResponder = _responder;
        _responder = async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/containers/scan-2/wait", StringComparison.Ordinal))
            {
                queuedScanStarted.TrySetResult();
                await queuedScanRelease.Task.WaitAsync(cancellationToken);
            }
            if (request.Method == HttpMethod.Delete)
                Assert.False(cancellationToken.IsCancellationRequested);
            return await originalResponder(request, cancellationToken);
        };
        var runningScan = _sut.ScanOrEnqueueAsync(Guid.NewGuid(), _testRootDirectory);
        var cancelledModuleId = Guid.NewGuid();
        var healthyModuleId = Guid.NewGuid();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True((await _sut.ScanOrEnqueueAsync(cancelledModuleId, _testRootDirectory, ScanPriority.Patch, cancellation.Token)).WasQueued);
            Assert.True((await _sut.ScanOrEnqueueAsync(healthyModuleId, _testRootDirectory, ScanPriority.Create)).WasQueued);
            release.TrySetResult();
            await runningScan.WaitAsync(TimeSpan.FromSeconds(5));
            await queuedScanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            if (callerCancels)
                cancellation.Cancel();
            else
                Assert.True(_sut.CancelScan(cancelledModuleId));

            var processed = await _moduleService.Results.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(healthyModuleId, processed.ModuleId);
            Assert.False(_moduleService.Results.Reader.TryRead(out _));
            Assert.False(_sut.CancelScan(cancelledModuleId));
            Assert.Equal(3, _requests.Count(request => request.StartsWith("DELETE ", StringComparison.Ordinal)));
        }
        finally
        {
            _sut.CancelScan(cancelledModuleId);
            _sut.CancelScan(healthyModuleId);
            release.TrySetResult();
            queuedScanRelease.TrySetResult();
            await runningScan.WaitAsync(TimeSpan.FromSeconds(5));
        }
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
            return JsonResponse(new { Id = "scan-" + Interlocked.Increment(ref _containerCount) });
        }
        if (path.EndsWith("/start", StringComparison.Ordinal) || request.Method == HttpMethod.Delete)
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        if (path.EndsWith("/wait", StringComparison.Ordinal))
            return JsonResponse(new { StatusCode = 0 });
        if (path.EndsWith("/logs", StringComparison.Ordinal))
            return LogResponse(ValidOutput);
        throw new InvalidOperationException("Unexpected Docker request: " + request.RequestUri);
    }

    private static HttpResponseMessage JsonResponse(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage LogResponse(string stdout, string stderr = "")
    {
        using var stream = new MemoryStream();
        foreach (var output in new[] { (Channel: (byte)1, Text: stdout), (Channel: (byte)2, Text: stderr) })
        {
            var payload = Encoding.UTF8.GetBytes(output.Text);
            var header = new byte[8];
            header[0] = output.Channel;
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), payload.Length);
            stream.Write(header);
            stream.Write(payload);
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(stream.ToArray()) };
    }

    private sealed class RecordingModuleService : IModuleService
    {
        public Channel<(Guid ModuleId, ModuleScanResultDto Result)> Results { get; } =
            Channel.CreateUnbounded<(Guid, ModuleScanResultDto)>();

        public Task ProcessQueuedScanResultAsync(Guid moduleId, ModuleScanResultDto result, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Results.Writer.TryWrite((moduleId, result));
            return Task.CompletedTask;
        }

        public Task<ModuleCreationResultDto> CreateModuleAsync(CreateModuleDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ModuleDetailDto> ConfirmEndpointsAsync(Guid moduleId, ConfirmEndpointsDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IEnumerable<ModuleDto>> GetAllModulesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ModuleDetailDto> GetModuleByIdAsync(Guid moduleId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IEnumerable<ModuleEndpointDto>> GetModuleEndpointsAsync(Guid moduleId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateModuleAsync(Guid moduleId, UpdateModuleDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ModuleCreationResultDto> UpdateModuleFilesAsync(Guid moduleId, UpdateModuleFilesDto file, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteModuleAsync(Guid moduleId, CancellationToken ct = default) => throw new NotSupportedException();
    }

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

    #endregion
}