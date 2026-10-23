using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;
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
        var ep1 = new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = module.Id, ModuleVersion = module.Versions.Single(), HttpMethod = HttpMethod.Get, EndpointPath = "/a" };
        var ep2 = new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = module.Id, ModuleVersion = module.Versions.Single(), HttpMethod = HttpMethod.Post, EndpointPath = "/b" };

        _context.Modules.Add(module);
        _context.ModuleEndpoints.AddRange(ep1, ep2);
        await _context.SaveChangesAsync();

        var pagedResult = await _sut.GetModuleSubEndpointsAsync(module.Id);
        var result = pagedResult.Items.ToList();

        Assert.Equal(2, pagedResult.TotalCount);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.EndpointPath == "/a" && e.HttpMethod == HttpMethod.Get);
        Assert.Contains(result, e => e.EndpointPath == "/b" && e.HttpMethod == HttpMethod.Post);
    }

    [Fact]
    [Trait("Feature", "GetModuleSubEndpoints")]
    public async Task GetModuleSubEndpointsAsync_FiltersBeforePaging_ReturnsOnlyTargetModuleEndpoints()
    {
        var otherModule = CreateTestModuleEntity("Other", "api/v1/other", 8005);
        var targetModule = CreateTestModuleEntity("Target", "api/v1/target", 8006);
        _context.Modules.AddRange(otherModule, targetModule);
        _context.ModuleEndpoints.AddRange(
            new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = otherModule.Id, ModuleVersion = otherModule.Versions.Single(), HttpMethod = HttpMethod.Get, EndpointPath = "/other-1" },
            new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = otherModule.Id, ModuleVersion = otherModule.Versions.Single(), HttpMethod = HttpMethod.Get, EndpointPath = "/other-2" },
            new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = targetModule.Id, ModuleVersion = targetModule.Versions.Single(), HttpMethod = HttpMethod.Get, EndpointPath = "/target-1" },
            new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = targetModule.Id, ModuleVersion = targetModule.Versions.Single(), HttpMethod = HttpMethod.Post, EndpointPath = "/target-2" });
        await _context.SaveChangesAsync();

        var page1 = await _sut.GetModuleSubEndpointsAsync(targetModule.Id, new PaginationQueryDto { Page = 1, PageSize = 1 });
        var page2 = await _sut.GetModuleSubEndpointsAsync(targetModule.Id, new PaginationQueryDto { Page = 2, PageSize = 1 });

        Assert.Equal(2, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Single(page1.Items);
        Assert.True(page1.HasNextPage);
        Assert.Single(page2.Items);
        Assert.True(page2.HasPreviousPage);
        Assert.NotEqual(page1.Items[0].EndpointPath, page2.Items[0].EndpointPath);
    }

    #endregion
}
