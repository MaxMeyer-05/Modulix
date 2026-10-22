using System.Net;

using Microsoft.EntityFrameworkCore;

using Modulix.Client.Models.Enums;
using Modulix.Database.Entities;
using Modulix.Tests.TestData;

namespace Modulix.Tests.Services;

/// <summary>
/// Queueing, priority ordering, cancellation, and background scan processing tests for <see cref="Modulix.Scanner.ModuleEndpointScanner"/>.
/// </summary>
public partial class ModuleEndpointScannerTests
{
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

            var queuedBeforeRelease = _sut.GetQueuedScans();
            Assert.Equal(2, queuedBeforeRelease.Count);
            Assert.Equal(patchModuleId, queuedBeforeRelease[0].ModuleId);
            Assert.Equal(ScanPriority.Patch, queuedBeforeRelease[0].Priority);
            Assert.False(queuedBeforeRelease[0].IsRunning);
            Assert.Equal(createModuleId, queuedBeforeRelease[1].ModuleId);
            Assert.Equal(ScanPriority.Create, queuedBeforeRelease[1].Priority);
            Assert.False(queuedBeforeRelease[1].IsRunning);

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
                BaseUrlPath = "api/queued",
                ContainerPort = 8080,
                CurrentVersion = "v1.0.0",
                Status = ModuleStatus.QueuedForScan
            });
            await _context.SaveChangesAsync();
        }
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockFirstScan(started, release);
        var originalResponder = _responder;
        _responder = (request, cancellationToken) => request.RequestUri!.AbsolutePath.EndsWith("/containers/scan-2/wait", StringComparison.Ordinal)
            ? Task.FromResult(DockerHttpTestHelper.JsonResponse(new { message = "Background scan failed." }, HttpStatusCode.InternalServerError))
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

            var snapshotWhileRunning = _sut.GetQueuedScans();
            Assert.Equal(2, snapshotWhileRunning.Count);
            Assert.Equal(cancelledModuleId, snapshotWhileRunning[0].ModuleId);
            Assert.Equal(ScanPriority.Patch, snapshotWhileRunning[0].Priority);
            Assert.True(snapshotWhileRunning[0].IsRunning);
            Assert.Equal(healthyModuleId, snapshotWhileRunning[1].ModuleId);
            Assert.Equal(ScanPriority.Create, snapshotWhileRunning[1].Priority);
            Assert.False(snapshotWhileRunning[1].IsRunning);

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
}
