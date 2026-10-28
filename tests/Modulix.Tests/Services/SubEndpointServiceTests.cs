using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;
using Modulix.Client.Models.Enums;
using Modulix.Database.Entities;
using Modulix.Services;
using Modulix.Tests.TestData;

namespace Modulix.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SubEndpointService"/>.
/// </summary>
[Trait("Category", "Services")]
[Trait("SubCategory", "SubEndpointService")]
public partial class SubEndpointServiceTests : ServiceTestBase
{
    private readonly SubEndpointService _sut;

    public SubEndpointServiceTests()
    {
        _sut = new SubEndpointService(
            _context,
            NullLogger<SubEndpointService>.Instance,
            _dockerService,
            _moduleEndpointExtension);
    }

    #region GetModuleSubEndpoints Tests

    [Fact]
    [Trait("Feature", "GetModuleSubEndpoints")]
    public async Task GetModuleSubEndpointsAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        var nonExistentId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.GetModuleSubEndpointsAsync(nonExistentId));

        Assert.Equal($"Module with ID '{nonExistentId}' was not found.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "GetModuleSubEndpoints")]
    public async Task GetModuleSubEndpointsAsync_ModuleExists_ReturnsAllSubEndpoints()
    {
        var module = CreateTestModuleEntity("Gateway", "api/v1/gw", 8004);
        var ep1 = new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = module.Id, ModuleVersion = module.Versions.Single(), HttpMethod = "GET", EndpointPath = "/a" };
        var ep2 = new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = module.Id, ModuleVersion = module.Versions.Single(), HttpMethod = "POST", EndpointPath = "/b" };

        _context.Modules.Add(module);
        _context.ModuleEndpoints.AddRange(ep1, ep2);
        await _context.SaveChangesAsync();

        var pagedResult = await _sut.GetModuleSubEndpointsAsync(module.Id);
        var result = pagedResult.Items.ToList();

        Assert.Equal(2, pagedResult.TotalCount);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.EndpointPath == "/a" && e.HttpMethod == "GET");
        Assert.Contains(result, e => e.EndpointPath == "/b" && e.HttpMethod == "POST");
    }

    [Fact]
    [Trait("Feature", "GetModuleSubEndpoints")]
    public async Task GetModuleSubEndpointsAsync_FiltersBeforePaging_ReturnsOnlyTargetModuleEndpoints()
    {
        var otherModule = CreateTestModuleEntity("Other", "api/v1/other", 8005);
        var targetModule = CreateTestModuleEntity("Target", "api/v1/target", 8006);
        _context.Modules.AddRange(otherModule, targetModule);
        _context.ModuleEndpoints.AddRange(
            new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = otherModule.Id, ModuleVersion = otherModule.Versions.Single(), HttpMethod = "GET", EndpointPath = "/other-1" },
            new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = otherModule.Id, ModuleVersion = otherModule.Versions.Single(), HttpMethod = "GET", EndpointPath = "/other-2" },
            new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = targetModule.Id, ModuleVersion = targetModule.Versions.Single(), HttpMethod = "GET", EndpointPath = "/target-1" },
            new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = targetModule.Id, ModuleVersion = targetModule.Versions.Single(), HttpMethod = "POST", EndpointPath = "/target-2" });
        await _context.SaveChangesAsync();

        var page1 = await _sut.GetModuleSubEndpointsAsync(targetModule.Id, pagination: new PaginationQueryDto { Page = 1, PageSize = 1 });
        var page2 = await _sut.GetModuleSubEndpointsAsync(targetModule.Id, pagination: new PaginationQueryDto { Page = 2, PageSize = 1 });

        Assert.Equal(2, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Single(page1.Items);
        Assert.True(page1.HasNextPage);
        Assert.Single(page2.Items);
        Assert.True(page2.HasPreviousPage);
        Assert.NotEqual(page1.Items[0].EndpointPath, page2.Items[0].EndpointPath);
    }

    #endregion

    #region UpdateSubEndpointStatus & DeleteSubEndpoint Tests

    [Fact]
    [Trait("Feature", "UpdateSubEndpointStatus")]
    public async Task UpdateSubEndpointStatusAsync_RunningModule_TogglesStatusAndSynchronizesProxy()
    {
        var module = CreateTestModuleEntity("ToggleModule", "api/v1/toggle", 8010);
        module.Status = ModuleStatus.Running;
        var ep1 = new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "GET",
            EndpointPath = "/one",
            Status = ModuleEndpointsStatus.Active
        };
        var ep2 = new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "POST",
            EndpointPath = "/two",
            Status = ModuleEndpointsStatus.Active
        };
        module.SubEndpoints.Add(ep1);
        module.SubEndpoints.Add(ep2);
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        await _sut.UpdateSubEndpointStatusAsync(module.Id, ep1.Id, new UpdateSubEndpointStatusDto { Status = ModuleEndpointsStatus.Inactive });

        var updatedEp1 = await _context.ModuleEndpoints.FindAsync(ep1.Id);
        Assert.Equal(ModuleEndpointsStatus.Inactive, updatedEp1!.Status);
        Assert.True(_reverseProxyService.UpsertedModules.TryGetValue(module.Id, out var proxyAfterDeactivate));
        Assert.Single(proxyAfterDeactivate!.Endpoints);
        Assert.Equal("/two", proxyAfterDeactivate.Endpoints[0].EndpointPath);

        await _sut.UpdateSubEndpointStatusAsync(module.Id, ep1.Id, new UpdateSubEndpointStatusDto { Status = ModuleEndpointsStatus.Active });

        updatedEp1 = await _context.ModuleEndpoints.FindAsync(ep1.Id);
        Assert.Equal(ModuleEndpointsStatus.Active, updatedEp1!.Status);
        Assert.True(_reverseProxyService.UpsertedModules.TryGetValue(module.Id, out var proxyAfterActivate));
        Assert.Equal(2, proxyAfterActivate!.Endpoints.Count);
    }

    [Theory]
    [InlineData(ModuleStatus.Stopped)]
    [InlineData(ModuleStatus.Failed)]
    [InlineData(ModuleStatus.Created)]
    [Trait("Feature", "UpdateSubEndpointStatus")]
    public async Task UpdateSubEndpointStatusAsync_ActivateWhenModuleNotRunning_ThrowsInvalidOperationException(ModuleStatus status)
    {
        var module = CreateTestModuleEntity("StoppedModule", "api/v1/stopped", 8011);
        module.Status = status;
        var ep = new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "GET",
            EndpointPath = "/health",
            Status = ModuleEndpointsStatus.Inactive
        };
        module.SubEndpoints.Add(ep);
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.UpdateSubEndpointStatusAsync(module.Id, ep.Id, new UpdateSubEndpointStatusDto { Status = ModuleEndpointsStatus.Active }));
    }

    [Fact]
    [Trait("Feature", "DeleteSubEndpoint")]
    public async Task DeleteSubEndpointAsync_ExistingEndpoint_RemovesEndpointAndPreservesRemainingProxyRoutes()
    {
        var module = CreateTestModuleEntity("DeleteEpModule", "api/v1/delete-ep", 8012);
        module.Status = ModuleStatus.Running;
        var epKeep = new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "GET",
            EndpointPath = "/keep",
            Status = ModuleEndpointsStatus.Active
        };
        var epDelete = new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "DELETE",
            EndpointPath = "/remove",
            Status = ModuleEndpointsStatus.Active
        };
        module.SubEndpoints.Add(epKeep);
        module.SubEndpoints.Add(epDelete);
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        await _sut.DeleteSubEndpointAsync(module.Id, epDelete.Id);

        Assert.Null(await _context.ModuleEndpoints.FindAsync(epDelete.Id));
        Assert.NotNull(await _context.ModuleEndpoints.FindAsync(epKeep.Id));
        Assert.True(_reverseProxyService.UpsertedModules.TryGetValue(module.Id, out var proxyDefinition));
        Assert.Single(proxyDefinition!.Endpoints);
        Assert.Equal("/keep", proxyDefinition.Endpoints[0].EndpointPath);
    }

    #endregion
}
