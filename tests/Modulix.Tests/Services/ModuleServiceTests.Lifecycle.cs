using Microsoft.EntityFrameworkCore;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;
using Modulix.Database.Entities;
using Modulix.ReverseProxy.Models;
using Modulix.Tests.TestData;

namespace Modulix.Tests.Services;

/// <summary>
/// Module update, deletion, container start/stop, and reverse-proxy synchronization tests for <see cref="Modulix.Services.ModuleService"/>.
/// </summary>
public partial class ModuleServiceTests
{
    #region UpdateModule Tests

    [Fact]
    [Trait("Feature", "UpdateModule")]
    public async Task UpdateModuleAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        var nonExistentId = Guid.NewGuid();
        var dto = new UpdateModuleDto { ModuleName = "Any Name" };

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdateModuleAsync(nonExistentId, dto));

        Assert.Equal($"Module with ID '{nonExistentId}' was not found.", exception.Message);
    }

    [Theory]
    [ClassData(typeof(ValidModuleUpdateTestData))]
    [Trait("Feature", "UpdateModule")]
    public async Task UpdateModuleAsync_MetadataProvided_UpdatesModuleSuccessfully(
        UpdateModuleDto dto,
        string expectedName,
        string? expectedDescription)
    {
        var module = CreateTestModuleEntity("Original Name", "api/v1/orig", 8005);
        module.Description = "Original Description";
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        await _sut.UpdateModuleAsync(module.Id, dto);

        var updated = await _context.Modules.FindAsync(module.Id);
        Assert.NotNull(updated);
        Assert.Equal(expectedName, updated.ModuleName);
        Assert.Equal(expectedDescription, updated.Description);
        Assert.Empty(_dockerService.Calls);
    }

    [Fact]
    [Trait("Feature", "UpdateModule")]
    public async Task UpdateModuleAsync_NoChanges_LeavesEntityStateUnchanged()
    {
        var module = CreateTestModuleEntity("Unchanged", "api/v1/unchanged", 8006);
        module.Description = "Same Description";
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var dto = new UpdateModuleDto
        {
            ModuleName = "Unchanged",
            Description = "Same Description"
        };

        await _sut.UpdateModuleAsync(module.Id, dto);

        Assert.Equal(EntityState.Unchanged, _context.Entry(module).State);
    }

    #endregion

    #region DeleteModule Tests

    [Fact]
    [Trait("Feature", "DeleteModule")]
    public async Task DeleteModuleAsync_ModuleNotFound_CompletesSilently()
    {
        await _sut.DeleteModuleAsync(Guid.NewGuid());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Feature", "DeleteModule")]
    public async Task DeleteModuleAsync_ModuleExists_DeletesStorageAndEndpoints(bool hasContainer)
    {
        var moduleDir = Path.Combine(_testRootDirectory, "module_to_delete");
        Directory.CreateDirectory(moduleDir);
        File.WriteAllText(Path.Combine(moduleDir, "test.txt"), "delete me");

        var module = CreateTestModuleEntity("To Delete", "api/v1/delete", 8009);
        module.Versions.Single().StoragePath = moduleDir;
        var dataDir = _directoryExtension.EnsureModuleDataDirectory(module.Id);
        File.WriteAllText(Path.Combine(dataDir, "app.db"), "sqlite-bytes");
        module.ContainerId = hasContainer ? "existing-container" : null;

        var endpoint = new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "GET",
            EndpointPath = "/status",
            Status = ModuleEndpointsStatus.Active
        };

        _context.Modules.Add(module);
        _context.ModuleEndpoints.Add(endpoint);
        await _context.SaveChangesAsync();

        await _sut.DeleteModuleAsync(module.Id);

        Assert.False(Directory.Exists(moduleDir));
        Assert.False(Directory.Exists(dataDir));
        Assert.Null(await _context.Modules.FindAsync(module.Id));
        Assert.Null(await _context.ModuleEndpoints.FindAsync(endpoint.Id));
        Assert.Equal(hasContainer ? ["existing-container"] : Array.Empty<string>(), _dockerService.RemovedContainerIds);
    }

    [Fact]
    [Trait("Feature", "DeleteModule")]
    public async Task DeleteModuleAsync_QueuedForScan_CancelsScanAndDeletesModule()
    {
        var moduleDir = Path.Combine(_testRootDirectory, "queued_module_to_delete");
        Directory.CreateDirectory(moduleDir);

        var module = CreateTestModuleEntity("Queued Delete", "api/v1/queued-delete", 8010);
        module.Versions.Single().StoragePath = moduleDir;
        module.Status = ModuleStatus.QueuedForScan;

        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        await _sut.DeleteModuleAsync(module.Id);

        Assert.Equal([module.Id], _endpointScanner.CancelledModuleIds);
        Assert.False(Directory.Exists(moduleDir));
    }

    #endregion

    #region StartModule & StopModule Tests

    [Fact]
    [Trait("Feature", "StartModule")]
    public async Task StartModuleAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        var nonExistentId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.StartModuleAsync(nonExistentId));

        Assert.Equal($"Module with ID '{nonExistentId}' was not found.", exception.Message);
    }

    [Theory]
    [InlineData(ModuleStatus.Running)]
    [InlineData(ModuleStatus.Starting)]
    [InlineData(ModuleStatus.QueuedForScan)]
    [InlineData(ModuleStatus.PendingConfirmation)]
    [Trait("Feature", "StartModule")]
    public async Task StartModuleAsync_InvalidStatus_ThrowsInvalidOperationException(ModuleStatus status)
    {
        var module = CreateTestModuleEntity("Invalid Start", "api/v1/invalid-start", 8030);
        module.Status = status;
        module.ContainerId = "existing-container";
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartModuleAsync(module.Id));

        Assert.Empty(_dockerService.Calls);
    }

    [Fact]
    [Trait("Feature", "StartModule")]
    public async Task StartModuleAsync_StoppedModuleWithContainer_StartsExistingContainerAndSynchronizesProxy()
    {
        var module = CreateTestModuleEntity("Stopped Module", "api/v1/stopped", 8031);
        module.Status = ModuleStatus.Stopped;
        module.ContainerId = "existing-container";
        module.SubEndpoints.Add(new ModuleEndpoint
        {
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "GET",
            EndpointPath = "/ping",
            Status = ModuleEndpointsStatus.Inactive
        });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var result = await _sut.StartModuleAsync(module.Id);

        Assert.Equal(ModuleStatus.Running, result.Status);
        Assert.Equal("existing-container", result.ContainerId);
        Assert.Equal(ModuleEndpointsStatus.Active, Assert.Single(result.Endpoints).Status);
        Assert.Equal(["start", "ready"], _dockerService.Calls);
        Assert.Contains(_reverseProxyService.GetConfig().Routes, route => route.ClusterId == $"module-cluster-{module.Id:N}");
    }

    [Fact]
    [Trait("Feature", "StartModule")]
    public async Task StartModuleAsync_ModuleWithoutContainer_BuildsAndStartsContainer()
    {
        var module = CreateTestModuleEntity("Unbuilt Module", "api/v1/unbuilt", 8032);
        module.Status = ModuleStatus.Created;
        module.ContainerId = null;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var result = await _sut.StartModuleAsync(module.Id);

        Assert.Equal(ModuleStatus.Running, result.Status);
        Assert.Equal("replacement-container", result.ContainerId);
        Assert.Equal(["build", "start", "ready"], _dockerService.Calls);
    }

    [Fact]
    [Trait("Feature", "StartModule")]
    public async Task StartModuleAsync_DockerStartFails_PersistsFailedStatus()
    {
        var module = CreateTestModuleEntity("Failing Start", "api/v1/failing-start", 8033);
        module.Status = ModuleStatus.Stopped;
        module.ContainerId = "existing-container";
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _dockerService.StartException = new InvalidOperationException("Container start failed.");

        var result = await _sut.StartModuleAsync(module.Id);

        Assert.Equal(ModuleStatus.Failed, result.Status);
        var persisted = await _context.Modules.AsNoTracking().SingleAsync(m => m.Id == module.Id);
        Assert.Equal(ModuleStatus.Failed, persisted.Status);
    }

    [Fact]
    [Trait("Feature", "StopModule")]
    public async Task StopModuleAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        var nonExistentId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.StopModuleAsync(nonExistentId));

        Assert.Equal($"Module with ID '{nonExistentId}' was not found.", exception.Message);
    }

    [Theory]
    [InlineData(ModuleStatus.Stopped)]
    [InlineData(ModuleStatus.Created)]
    [InlineData(ModuleStatus.Failed)]
    [InlineData(ModuleStatus.QueuedForScan)]
    [InlineData(ModuleStatus.PendingConfirmation)]
    [Trait("Feature", "StopModule")]
    public async Task StopModuleAsync_InvalidStatus_ThrowsInvalidOperationException(ModuleStatus status)
    {
        var module = CreateTestModuleEntity("Invalid Stop", "api/v1/invalid-stop", 8034);
        module.Status = status;
        module.ContainerId = "existing-container";
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StopModuleAsync(module.Id));

        Assert.Empty(_dockerService.Calls);
    }

    [Fact]
    [Trait("Feature", "StopModule")]
    public async Task StopModuleAsync_RunningModule_StopsContainerAndRemovesProxyRoutes()
    {
        var module = CreateTestModuleEntity("Running Module", "api/v1/running", 8035);
        module.Status = ModuleStatus.Running;
        module.ContainerId = "running-container";
        module.SubEndpoints.Add(new ModuleEndpoint
        {
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "GET",
            EndpointPath = "/ping",
            Status = ModuleEndpointsStatus.Active
        });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _reverseProxyService.UpsertModule(new ProxyModuleDefinition
        {
            ModuleId = module.Id,
            BaseUrlPath = module.BaseUrlPath,
            ContainerPort = module.ContainerPort,
            Endpoints = [new ProxyEndpointDefinition { HttpMethod = "GET", EndpointPath = "/ping" }]
        });

        var result = await _sut.StopModuleAsync(module.Id);

        Assert.Equal(ModuleStatus.Stopped, result.Status);
        Assert.Equal(ModuleEndpointsStatus.Inactive, Assert.Single(result.Endpoints).Status);
        Assert.Equal(["stop:running-container"], _dockerService.Calls);
        Assert.DoesNotContain(_reverseProxyService.GetConfig().Routes, route => route.ClusterId == $"module-cluster-{module.Id:N}");
    }

    [Fact]
    [Trait("Feature", "GetModuleStats")]
    public async Task GetModuleStatsAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        var nonExistentId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.GetModuleStatsAsync(nonExistentId));

        Assert.Equal($"Module with ID '{nonExistentId}' was not found.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "GetModuleStats")]
    public async Task GetModuleStatsAsync_ModuleWithoutContainer_ReturnsDefaultStoppedStats()
    {
        var module = CreateTestModuleEntity("No Container Stats", "api/v1/no-container-stats", 8036);
        module.ContainerId = null;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var stats = await _sut.GetModuleStatsAsync(module.Id);

        Assert.False(stats.IsRunning);
        Assert.Equal(0.0, stats.CpuUsagePercent);
        Assert.Equal(0.0, stats.MemoryUsageMiB);
        Assert.Empty(_dockerService.Calls);
    }

    [Fact]
    [Trait("Feature", "GetModuleStats")]
    public async Task GetModuleStatsAsync_ModuleWithContainer_ReturnsDockerContainerStats()
    {
        var module = CreateTestModuleEntity("Container Stats", "api/v1/container-stats", 8037);
        module.Status = ModuleStatus.Running;
        module.ContainerId = "stats-container";
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        _dockerService.ContainerStatsResult = new Modulix.Docker.Models.ContainerStats
        {
            IsRunning = true,
            CpuUsagePercent = 34.5,
            MemoryUsageMiB = 128.25,
            MemoryLimitMiB = 1024.0,
            UptimeSeconds = 3665.0,
            ExitCode = 0
        };

        var stats = await _sut.GetModuleStatsAsync(module.Id);

        Assert.True(stats.IsRunning);
        Assert.Equal(34.5, stats.CpuUsagePercent);
        Assert.Equal(128.25, stats.MemoryUsageMiB);
        Assert.Equal(1024.0, stats.MemoryLimitMiB);
        Assert.Equal(3665.0, stats.UptimeSeconds);
        Assert.Equal(0, stats.ExitCode);
        Assert.Equal(["stats:stats-container"], _dockerService.Calls);
    }

    #endregion

    #region ReverseProxy Synchronization Tests

    [Fact]
    [Trait("Feature", "ReverseProxySynchronization")]
    public async Task ModuleLifecycle_SynchronizesRoutesWithReverseProxy()
    {
        _endpointScanner.DiscoveredEndpoints =
        [
            new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/reports" },
            new DiscoveredSubEndpointDto { HttpMethod = "POST", EndpointPath = "/export" }
        ];

        var createResult = await _sut.CreateModuleAsync(new CreateModuleDto
        {
            ModuleName = "Analytics Module",
            BaseUrlPath = "/analytics/",
            ContainerPort = 8095,
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile(),
            InitialEndpoints = [new CreateModuleSubEndpointDto { HttpMethod = "GET", EndpointPath = "/reports" }]
        });

        var moduleId = createResult.Module.Id;
        Assert.Equal(ModuleStatus.PendingConfirmation, createResult.Module.Status);
        Assert.Empty(_reverseProxyService.GetConfig().Clusters);
        Assert.Empty(_reverseProxyService.GetConfig().Routes);

        var confirmResult = await _subEndpointService.ConfirmSubEndpointsAsync(moduleId, new ConfirmEndpointsDto
        {
            ConfirmedEndpoints =
            [
                new SubEndpointDiscrepancyReportDto
                {
                    ModuleId = moduleId,
                    ExtraEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = "POST", EndpointPath = "/export" }]
                }
            ]
        });

        Assert.Equal(ModuleStatus.Running, confirmResult.Status);
        var activeConfig = _reverseProxyService.GetConfig();
        var cluster = Assert.Single(activeConfig.Clusters);
        Assert.Equal($"module-cluster-{moduleId:N}", cluster.ClusterId);
        Assert.Equal("http://127.0.0.1:8095", cluster.Destinations!["container"].Address);
        Assert.Equal(2, activeConfig.Routes.Count);
        Assert.Contains(activeConfig.Routes, r => r.Match.Path == "/analytics/reports" && r.Match.Methods!.Contains("GET"));
        Assert.Contains(activeConfig.Routes, r => r.Match.Path == "/analytics/export" && r.Match.Methods!.Contains("POST"));

        _endpointScanner.WasQueued = true;
        var queuedUpdateResult = await _moduleVersionService.CreateModuleVersionAsync(moduleId, new ModuleVersionCreateDto
        {
            Version = "v1.1.0",
            ModuleFile = CreateDummyZipFile()
        });

        Assert.Equal(ModuleStatus.QueuedForScan, queuedUpdateResult.Module.Status);
        Assert.Empty(_reverseProxyService.GetConfig().Clusters);
        Assert.Empty(_reverseProxyService.GetConfig().Routes);

        await _scannerService.ProcessQueuedScanResultAsync(moduleId, new ModuleScanResultDto
        {
            EntryAssemblyFileName = "module.dll",
            DiscoveredEndpoints =
            [
                new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/reports" },
                new DiscoveredSubEndpointDto { HttpMethod = "POST", EndpointPath = "/export" }
            ]
        });

        Assert.Single(_reverseProxyService.GetConfig().Clusters);
        Assert.Equal(2, _reverseProxyService.GetConfig().Routes.Count);

        await _sut.DeleteModuleAsync(moduleId);
        Assert.Empty(_reverseProxyService.GetConfig().Clusters);
        Assert.Empty(_reverseProxyService.GetConfig().Routes);
    }

    #endregion
}
