using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;
using Modulix.Database.Entities;
using Modulix.Services;
using Modulix.Tests.TestData;

namespace Modulix.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ScannerService"/>.
/// </summary>
[Trait("Category", "Services")]
[Trait("SubCategory", "ScannerService")]
public class ScannerServiceTests : ServiceTestBase
{
    private readonly ScannerService _sut;

    public ScannerServiceTests()
    {
        _sut = new ScannerService(
            _context,
            NullLogger<ScannerService>.Instance,
            _endpointScanner,
            _moduleEndpointExtension);
    }

    #region ProcessQueuedScanResult Tests

    [Fact]
    [Trait("Feature", "ProcessQueuedScanResult")]
    public async Task ProcessQueuedScanResultAsync_DeletedModule_DoesNotRecreateModuleOrBuildContainer()
    {
        await _sut.ProcessQueuedScanResultAsync(Guid.NewGuid(), new ModuleScanResultDto
        {
            EntryAssemblyFileName = "scanned.dll",
            DiscoveredEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = HttpMethod.Get, EndpointPath = "/health" }]
        });

        Assert.Empty(await _context.Modules.ToListAsync());
        Assert.Empty(await _context.ModuleEndpoints.ToListAsync());
        Assert.Empty(_dockerService.Calls);
    }

    [Fact]
    [Trait("Feature", "ProcessQueuedScanResult")]
    public async Task ProcessQueuedScanResultAsync_ModuleAlreadyRunning_DoesNotOverwriteActiveVersion()
    {
        var module = CreateTestModuleEntity("Running module", "api/running", 8080);
        module.Status = ModuleStatus.Running;
        module.ContainerId = "active-container";
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        await _sut.ProcessQueuedScanResultAsync(module.Id, new ModuleScanResultDto
        {
            EntryAssemblyFileName = "stale.dll",
            DiscoveredEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = HttpMethod.Get, EndpointPath = "/stale" }]
        });

        var persisted = await _context.Modules.AsNoTracking().Include(item => item.SubEndpoints).Include(item => item.Versions).SingleAsync();
        Assert.Equal(ModuleStatus.Running, persisted.Status);
        Assert.Equal("active-container", persisted.ContainerId);
        Assert.Equal("module.dll", persisted.Versions.Single().ModuleEntryAssemblyFileName);
        Assert.Empty(persisted.SubEndpoints);
        Assert.Empty(_dockerService.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Feature", "ProcessQueuedScanResult")]
    public async Task ProcessQueuedScanResultAsync_DiscoveredEntryAssembly_PersistsActualAssemblyBeforeProvisioning(bool hasManualEndpoint)
    {
        var module = CreateTestModuleEntity("Queued module", "api/queued", 8080);
        module.Status = ModuleStatus.QueuedForScan;
        module.Versions.Single().ModuleEntryAssemblyFileName = "placeholder.dll";
        if (hasManualEndpoint)
            module.SubEndpoints.Add(new ModuleEndpoint
            {
                ModuleId = module.Id,
                ModuleVersion = module.Versions.Single(),
                HttpMethod = HttpMethod.Get,
                EndpointPath = "/health",
                Status = ModuleEndpointsStatus.PendingConfirmation
            });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        await _sut.ProcessQueuedScanResultAsync(module.Id, new ModuleScanResultDto
        {
            EntryAssemblyFileName = "scanned.dll",
            DiscoveredEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = HttpMethod.Get, EndpointPath = "/health" }]
        });

        var persisted = await _context.Modules.AsNoTracking().Include(item => item.SubEndpoints).Include(item => item.Versions).SingleAsync(item => item.Id == module.Id);
        Assert.Equal("scanned.dll", persisted.Versions.Single().ModuleEntryAssemblyFileName);
        Assert.Equal(ModuleStatus.Running, persisted.Status);
        Assert.Equal("replacement-container", persisted.ContainerId);
        Assert.Equal(ModuleEndpointsStatus.Active, Assert.Single(persisted.SubEndpoints).Status);
        Assert.Equal("scanned.dll", _dockerService.BuiltEntryAssembly);
        Assert.Equal(new[] { "build", "start", "ready" }, _dockerService.Calls);
    }

    [Fact]
    [Trait("Feature", "ProcessQueuedScanResult")]
    public async Task ProcessQueuedScanResultAsync_EndpointDiscrepancy_PersistsPendingRoutesWithoutStartingContainer()
    {
        var module = CreateTestModuleEntity("Queued module", "api/queued", 8080);
        module.Status = ModuleStatus.QueuedForScan;
        module.SubEndpoints.Add(new ModuleEndpoint
        {
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = HttpMethod.Get,
            EndpointPath = "/expected",
            Status = ModuleEndpointsStatus.PendingConfirmation
        });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        await _sut.ProcessQueuedScanResultAsync(module.Id, new ModuleScanResultDto
        {
            EntryAssemblyFileName = "module.dll",
            DiscoveredEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = HttpMethod.Post, EndpointPath = "/discovered" }]
        });

        var persisted = await _context.Modules.AsNoTracking().Include(item => item.SubEndpoints).SingleAsync(item => item.Id == module.Id);
        Assert.Equal(ModuleStatus.PendingConfirmation, persisted.Status);
        Assert.Equal(new[] { "/discovered", "/expected" }, persisted.SubEndpoints.Select(endpoint => endpoint.EndpointPath).Order());
        Assert.All(persisted.SubEndpoints, endpoint => Assert.Equal(ModuleEndpointsStatus.PendingConfirmation, endpoint.Status));
        Assert.Empty(_dockerService.Calls);
    }

    #endregion

    #region GetScanQueue Tests

    [Fact]
    [Trait("Feature", "GetScanQueue")]
    public async Task GetScanQueueAsync_EmptyQueue_ReturnsEmptyCollection()
    {
        var result = await _sut.GetScanQueueAsync();

        Assert.Empty(result);
    }

    [Fact]
    [Trait("Feature", "GetScanQueue")]
    public async Task GetScanQueueAsync_QueuedItems_ResolvesModuleNamesAndPreservesOrderAndPriority()
    {
        var patchModule = CreateTestModuleEntity("PatchModule", "api/patch", 8081);
        patchModule.Status = ModuleStatus.QueuedForScan;
        var createModule = CreateTestModuleEntity("CreateModule", "api/create", 8082);
        createModule.Status = ModuleStatus.QueuedForScan;
        _context.Modules.AddRange(patchModule, createModule);
        await _context.SaveChangesAsync();

        var unknownModuleId = Guid.NewGuid();
        _endpointScanner.QueuedScans =
        [
            new ModuleScanQueueItemDto { ModuleId = patchModule.Id, Priority = ScanPriority.Patch, IsRunning = true },
            new ModuleScanQueueItemDto { ModuleId = createModule.Id, Priority = ScanPriority.Create, IsRunning = false },
            new ModuleScanQueueItemDto { ModuleId = unknownModuleId, Priority = ScanPriority.Create, IsRunning = false }
        ];

        var result = (await _sut.GetScanQueueAsync()).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(patchModule.Id, result[0].ModuleId);
        Assert.Equal("PatchModule", result[0].ModuleName);
        Assert.Equal(ScanPriority.Patch, result[0].Priority);
        Assert.True(result[0].IsRunning);

        Assert.Equal(createModule.Id, result[1].ModuleId);
        Assert.Equal("CreateModule", result[1].ModuleName);
        Assert.Equal(ScanPriority.Create, result[1].Priority);
        Assert.False(result[1].IsRunning);

        Assert.Equal(unknownModuleId, result[2].ModuleId);
        Assert.Equal(string.Empty, result[2].ModuleName);
        Assert.Equal(ScanPriority.Create, result[2].Priority);
        Assert.False(result[2].IsRunning);
    }

    #endregion
}
