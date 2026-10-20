using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

using Modulix.Database.DbContexts;
using Modulix.Database.Entities;

using Modulix.Docker.Services;
using Modulix.Extensions;
using Modulix.ReverseProxy.Services;
using Modulix.Scanner.Interfaces;
using Modulix.Services;

namespace Modulix.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ScannerService"/>.
/// </summary>
[Trait("Category", "Services")]
[Trait("SubCategory", "ScannerService")]
public class ScannerServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServerContext _context;
    private readonly FakeModuleEndpointScanner _endpointScanner;
    private readonly FakeDockerService _dockerService;
    private readonly ReverseProxyService _reverseProxyService;
    private readonly ScannerService _sut;

    #region Setup & Teardown

    public ScannerServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ServerContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ServerContext(options);
        _context.Database.EnsureCreated();

        _endpointScanner = new FakeModuleEndpointScanner();
        _dockerService = new FakeDockerService();
        _reverseProxyService = new ReverseProxyService(NullLogger<ReverseProxyService>.Instance);

        _sut = new ScannerService(
            _context,
            NullLogger<ScannerService>.Instance,
            _endpointScanner,
            new ModuleEndpointExtension(_context, NullLogger<ModuleEndpointExtension>.Instance, _dockerService, _reverseProxyService));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion

    #region ProcessQueuedScanResult Tests

    [Fact]
    [Trait("Feature", "ProcessQueuedScanResult")]
    public async Task ProcessQueuedScanResultAsync_DeletedModule_DoesNotRecreateModuleOrBuildContainer()
    {
        await _sut.ProcessQueuedScanResultAsync(Guid.NewGuid(), new ModuleScanResultDto
        {
            EntryAssemblyFileName = "scanned.dll",
            DiscoveredEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/health" }]
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
            DiscoveredEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/stale" }]
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
                HttpMethod = "GET",
                EndpointPath = "/health",
                Status = ModuleEndpointsStatus.PendingConfirmation
            });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        await _sut.ProcessQueuedScanResultAsync(module.Id, new ModuleScanResultDto
        {
            EntryAssemblyFileName = "scanned.dll",
            DiscoveredEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/health" }]
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
            HttpMethod = "GET",
            EndpointPath = "/expected",
            Status = ModuleEndpointsStatus.PendingConfirmation
        });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        await _sut.ProcessQueuedScanResultAsync(module.Id, new ModuleScanResultDto
        {
            EntryAssemblyFileName = "module.dll",
            DiscoveredEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = "POST", EndpointPath = "/discovered" }]
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

    #region Test Fakes & Helpers

    private static Module CreateTestModuleEntity(string name, string basePath, int port)
    {
        var module = new Module
        {
            Id = Guid.NewGuid(),
            ModuleName = name,
            Description = "Description for " + name,
            BaseUrlPath = basePath.TrimStart('/'),
            ContainerPort = port,
            CurrentVersion = "v1.0.0",
            Status = ModuleStatus.Created,
            CreatedAtUtc = DateTime.UtcNow
        };
        module.Versions.Add(new ModuleVersion
        {
            ModuleId = module.Id,
            Version = "v1.0.0",
            StoragePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            ModuleEntryAssemblyFileName = "module.dll"
        });
        return module;
    }

    private sealed class FakeModuleEndpointScanner : IModuleEndpointScanner
    {
        public List<ModuleScanQueueItemDto> QueuedScans { get; set; } = [];

        public bool CancelScan(Guid moduleId) => false;

        public IReadOnlyList<ModuleScanQueueItemDto> GetQueuedScans() => QueuedScans;

        public Task<ScanEnqueueResponse> ScanOrEnqueueAsync(
            Guid moduleId, string moduleDirectoryPath, ScanPriority priority = ScanPriority.Create, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeDockerService : IDockerService
    {
        public List<string> Calls { get; } = [];
        public string? BuiltEntryAssembly { get; private set; }

        public Task<string> BuildContainerAsync(Guid moduleId, string storagePath, string entryDllName, int containerPort, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add("build");
            BuiltEntryAssembly = entryDllName;
            return Task.FromResult("replacement-container");
        }

        public Task RunContainerAsync(string containerId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add("start");
            return Task.CompletedTask;
        }

        public Task WaitUntilReadyAsync(string containerId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add("ready");
            return Task.CompletedTask;
        }

        public Task StopContainerAsync(string containerId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveContainerAsync(string containerId, Guid moduleId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TryRemoveModuleContainerAsync(string containerId, Guid moduleId) => throw new NotSupportedException();
    }

    #endregion
}
