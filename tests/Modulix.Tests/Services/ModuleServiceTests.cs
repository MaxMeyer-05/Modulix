using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;
using Modulix.Database.Entities;
using Modulix.ReverseProxy.Models;
using Modulix.Services;
using Modulix.Tests.TestData;

namespace Modulix.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ModuleService"/>.
/// </summary>
[Trait("Category", "Services")]
[Trait("SubCategory", "ModuleService")]
public partial class ModuleServiceTests : ServiceTestBase
{
    private readonly SubEndpointService _subEndpointService;
    private readonly ScannerService _scannerService;
    private readonly ModuleVersionService _moduleVersionService;
    private readonly ModuleService _sut;

    public ModuleServiceTests()
    {
        _subEndpointService = new SubEndpointService(
            _context,
            NullLogger<SubEndpointService>.Instance,
            _dockerService,
            _moduleEndpointExtension);
        _scannerService = new ScannerService(
            _context,
            NullLogger<ScannerService>.Instance,
            _endpointScanner,
            _moduleEndpointExtension);
        _moduleVersionService = new ModuleVersionService(
            _context,
            NullLogger<ModuleVersionService>.Instance,
            _dockerService,
            _endpointScanner,
            _directoryExtension,
            _moduleEndpointExtension);

        _sut = new ModuleService(
            _context,
            NullLogger<ModuleService>.Instance,
            _moduleOptions,
            _dockerService,
            _endpointScanner,
            _directoryExtension,
            _moduleEndpointExtension,
            _reverseProxyService);
    }

    #region GetAllModules Tests

    [Fact]
    [Trait("Feature", "GetAllModules")]
    public async Task GetAllModulesAsync_NoModulesInDatabase_ReturnsEmptyCollection()
    {
        // Act
        var result = await _sut.GetAllModulesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.False(result.HasPreviousPage);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    [Trait("Feature", "GetAllModules")]
    public async Task GetAllModulesAsync_MultipleModulesExist_ReturnsMappedDtos()
    {
        // Arrange
        var module1 = CreateTestModuleEntity("Module 1", "api/v1/m1", 8001);
        var module2 = CreateTestModuleEntity("Module 2", "api/v1/m2", 8002);
        _context.Modules.AddRange(module1, module2);
        await _context.SaveChangesAsync();

        // Act
        var pagedResult = await _sut.GetAllModulesAsync();
        var result = pagedResult.Items.ToList();

        // Assert
        Assert.Equal(2, pagedResult.TotalCount);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, m => m.Id == module1.Id && m.ModuleName == "Module 1");
        Assert.Contains(result, m => m.Id == module2.Id && m.ModuleName == "Module 2");
    }

    [Fact]
    [Trait("Feature", "GetAllModules")]
    public async Task GetAllModulesAsync_CustomPagination_ReturnsRequestedPageAndMetadata()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var module1 = CreateTestModuleEntity("Module 1", "api/v1/m1", 8001);
        module1.CreatedAtUtc = baseTime.AddMinutes(-3);
        var module2 = CreateTestModuleEntity("Module 2", "api/v1/m2", 8002);
        module2.CreatedAtUtc = baseTime.AddMinutes(-2);
        var module3 = CreateTestModuleEntity("Module 3", "api/v1/m3", 8003);
        module3.CreatedAtUtc = baseTime.AddMinutes(-1);
        _context.Modules.AddRange(module1, module2, module3);
        await _context.SaveChangesAsync();

        // Act
        var page1 = await _sut.GetAllModulesAsync(new PaginationQueryDto { Page = 1, PageSize = 2 });
        var page2 = await _sut.GetAllModulesAsync(new PaginationQueryDto { Page = 2, PageSize = 2 });
        var outOfRangePage = await _sut.GetAllModulesAsync(new PaginationQueryDto { Page = 5, PageSize = 2 });

        // Assert
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(2, page1.Items.Count);
        Assert.False(page1.HasPreviousPage);
        Assert.True(page1.HasNextPage);

        Assert.Single(page2.Items);
        Assert.True(page2.HasPreviousPage);
        Assert.False(page2.HasNextPage);

        Assert.Empty(outOfRangePage.Items);
        Assert.Equal(3, outOfRangePage.TotalCount);
    }

    [Fact]
    [Trait("Feature", "GetAllModules")]
    public async Task GetAllModulesAsync_RunningModuleWithCrashedContainer_TransitionsToFailedAndRemovesProxyRoutes()
    {
        var module = CreateTestModuleEntity("Crashed Module", "api/v1/crashed-list", 8040);
        module.Status = ModuleStatus.Running;
        module.ContainerId = "crashed-container";
        module.SubEndpoints.Add(new ModuleEndpoint
        {
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "GET",
            EndpointPath = "/status",
            Status = ModuleEndpointsStatus.Active
        });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _reverseProxyService.UpsertModule(new Modulix.ReverseProxy.Models.ProxyModuleDefinition
        {
            ModuleId = module.Id,
            BaseUrlPath = module.BaseUrlPath,
            ContainerPort = module.ContainerPort,
            Endpoints = [new Modulix.ReverseProxy.Models.ProxyEndpointDefinition { HttpMethod = "GET", EndpointPath = "/status" }]
        });
        _dockerService.IsContainerRunning = false;

        var pagedResult = await _sut.GetAllModulesAsync();

        var returned = Assert.Single(pagedResult.Items);
        Assert.Equal(ModuleStatus.Failed, returned.Status);
        var persisted = await _context.Modules.AsNoTracking().SingleAsync(m => m.Id == module.Id);
        Assert.Equal(ModuleStatus.Failed, persisted.Status);
        Assert.DoesNotContain(_reverseProxyService.GetConfig().Routes, route => route.ClusterId == $"module-cluster-{module.Id:N}");
    }

    #endregion

    #region GetModuleById Tests

    [Fact]
    [Trait("Feature", "GetModuleById")]
    public async Task GetModuleByIdAsync_RunningModuleWithCrashedContainer_TransitionsToFailedAndRemovesProxyRoutes()
    {
        var module = CreateTestModuleEntity("Crashed Detail", "api/v1/crashed-detail", 8041);
        module.Status = ModuleStatus.Running;
        module.ContainerId = "crashed-detail-container";
        module.SubEndpoints.Add(new ModuleEndpoint
        {
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "GET",
            EndpointPath = "/health",
            Status = ModuleEndpointsStatus.Active
        });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _reverseProxyService.UpsertModule(new Modulix.ReverseProxy.Models.ProxyModuleDefinition
        {
            ModuleId = module.Id,
            BaseUrlPath = module.BaseUrlPath,
            ContainerPort = module.ContainerPort,
            Endpoints = [new Modulix.ReverseProxy.Models.ProxyEndpointDefinition { HttpMethod = "GET", EndpointPath = "/health" }]
        });
        _dockerService.IsContainerRunning = false;

        var result = await _sut.GetModuleByIdAsync(module.Id);

        Assert.Equal(ModuleStatus.Failed, result.Status);
        var persisted = await _context.Modules.AsNoTracking().SingleAsync(m => m.Id == module.Id);
        Assert.Equal(ModuleStatus.Failed, persisted.Status);
        Assert.DoesNotContain(_reverseProxyService.GetConfig().Routes, route => route.ClusterId == $"module-cluster-{module.Id:N}");
    }

    [Fact]
    [Trait("Feature", "GetModuleById")]
    public async Task GetModuleByIdAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.GetModuleByIdAsync(nonExistentId));

        Assert.Equal($"Module with ID '{nonExistentId}' was not found.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "GetModuleById")]
    public async Task GetModuleByIdAsync_ModuleExists_ReturnsModuleDetailWithSubEndpoints()
    {
        // Arrange
        var module = CreateTestModuleEntity("Metrics", "api/v1/metrics", 8003);
        module.SubEndpoints.Add(new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "GET",
            EndpointPath = "/prometheus",
            Status = ModuleEndpointsStatus.Active
        });

        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetModuleByIdAsync(module.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(module.Id, result.Id);
        Assert.Equal("Metrics", result.ModuleName);
        Assert.Single(result.Endpoints);
        Assert.Equal("/prometheus", result.Endpoints.First().EndpointPath);
    }

    #endregion

    #region GetModuleContainerLogs Tests

    [Fact]
    [Trait("Feature", "GetModuleContainerLogs")]
    public async Task GetModuleContainerLogsAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        var nonExistentId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.GetModuleContainerLogsAsync(nonExistentId));

        Assert.Equal($"Module with ID '{nonExistentId}' was not found.", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Feature", "GetModuleContainerLogs")]
    public async Task GetModuleContainerLogsAsync_NoContainerAssigned_ThrowsInvalidOperationException(string? containerId)
    {
        var module = CreateTestModuleEntity("NoContainer", "api/v1/nocontainer", 8110);
        module.ContainerId = containerId;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.GetModuleContainerLogsAsync(module.Id));

        Assert.Equal($"Module with ID '{module.Id}' does not have an associated container.", exception.Message);
        Assert.Empty(_dockerService.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [Trait("Feature", "GetModuleContainerLogs")]
    public async Task GetModuleContainerLogsAsync_InvalidTail_ThrowsArgumentException(int invalidTail)
    {
        var module = CreateTestModuleEntity("InvalidTail", "api/v1/invalidtail", 8111);
        module.ContainerId = "container-123";
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.GetModuleContainerLogsAsync(module.Id, invalidTail));

        Assert.Empty(_dockerService.Calls);
    }

    [Fact]
    [Trait("Feature", "GetModuleContainerLogs")]
    public async Task GetModuleContainerLogsAsync_ValidContainer_ReturnsLogsFromDockerService()
    {
        var module = CreateTestModuleEntity("WithLogs", "api/v1/withlogs", 8112);
        module.ContainerId = "container-abc";
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _dockerService.ContainerLogsResult = "2026-10-22T08:00:00Z info: Started";

        var logs = await _sut.GetModuleContainerLogsAsync(module.Id, tail: 250);

        Assert.Equal("2026-10-22T08:00:00Z info: Started", logs);
        Assert.Equal(["logs:container-abc:250"], _dockerService.Calls);
    }

    #endregion
}