using Microsoft.EntityFrameworkCore;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;
using Modulix.Database.Entities;

namespace Modulix.Tests.Services;

/// <summary>
/// Endpoint confirmation tests for <see cref="Modulix.Services.SubEndpointService"/>.
/// </summary>
public partial class SubEndpointServiceTests
{
    [Fact]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmSubEndpointsAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        var nonExistentId = Guid.NewGuid();
        var dto = new ConfirmEndpointsDto();

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.ConfirmSubEndpointsAsync(nonExistentId, dto));

        Assert.Equal($"Module with ID '{nonExistentId}' was not found.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmSubEndpointsAsync_ModuleNotInPendingConfirmationStatus_ThrowsInvalidOperationException()
    {
        var module = CreateTestModuleEntity("Active Module", "api/v1/active", 8080);
        module.Status = ModuleStatus.Created;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ConfirmSubEndpointsAsync(module.Id, new ConfirmEndpointsDto()));

        Assert.Equal($"Module '{module.Id}' is not in PendingConfirmation status.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmSubEndpointsAsync_ValidDiscrepancies_ActivatesMissingAndRemovesExtraEndpoints()
    {
        var module = CreateTestModuleEntity("Pending Module", "api/v1/pending", 8080);
        module.Status = ModuleStatus.PendingConfirmation;

        var missingEndpoint = new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "GET",
            EndpointPath = "/health",
            Status = ModuleEndpointsStatus.PendingConfirmation
        };

        var extraEndpoint = new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "POST",
            EndpointPath = "/legacy",
            Status = ModuleEndpointsStatus.PendingConfirmation
        };

        module.SubEndpoints.Add(missingEndpoint);
        module.SubEndpoints.Add(extraEndpoint);

        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var confirmDto = new ConfirmEndpointsDto
        {
            ConfirmedEndpoints =
            [
                new SubEndpointDiscrepancyReportDto
                {
                    ModuleId = module.Id,
                    MissingEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = missingEndpoint.HttpMethod, EndpointPath = missingEndpoint.EndpointPath }],
                    ExtraEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = extraEndpoint.HttpMethod, EndpointPath = extraEndpoint.EndpointPath }]
                }
            ]
        };

        var result = await _sut.ConfirmSubEndpointsAsync(module.Id, confirmDto);

        Assert.Equal(ModuleStatus.Running, result.Status);

        var updatedExtra = await _context.ModuleEndpoints.FindAsync(extraEndpoint.Id);
        Assert.NotNull(updatedExtra);
        Assert.Equal(ModuleEndpointsStatus.Active, updatedExtra.Status);

        var removedMissing = await _context.ModuleEndpoints.FindAsync(missingEndpoint.Id);
        Assert.Null(removedMissing);
    }

    [Fact]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmSubEndpointsAsync_SamePathWithDifferentHttpMethod_DoesNotConfirmEndpoint()
    {
        var module = CreateTestModuleEntity("Pending Module", "api/v1/pending-method", 8082);
        module.Status = ModuleStatus.PendingConfirmation;

        var pendingEndpoint = new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "GET",
            EndpointPath = "/health",
            Status = ModuleEndpointsStatus.PendingConfirmation
        };

        module.SubEndpoints.Add(pendingEndpoint);
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var confirmDto = new ConfirmEndpointsDto
        {
            ConfirmedEndpoints =
            [
                new SubEndpointDiscrepancyReportDto
                {
                    ModuleId = module.Id,
                    MissingEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = "POST", EndpointPath = "/health" }]
                }
            ]
        };

        var result = await _sut.ConfirmSubEndpointsAsync(module.Id, confirmDto);

        var updatedEndpoint = await _context.ModuleEndpoints.FindAsync(pendingEndpoint.Id);
        Assert.NotNull(updatedEndpoint);
        Assert.Equal(ModuleEndpointsStatus.PendingConfirmation, updatedEndpoint.Status);
        Assert.Equal(ModuleStatus.PendingConfirmation, result.Status);
        Assert.Empty(_dockerService.Calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmSubEndpointsAsync_IncompleteConfirmation_StartsOnlyAfterLastDecision(
        bool confirmMissing, bool confirmExtra)
    {
        var module = CreateTestModuleEntity("Partial Confirmation", "api/v1/partial", 8083);
        module.Status = ModuleStatus.PendingConfirmation;
        module.SubEndpoints.Add(new ModuleEndpoint
        {
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "GET",
            EndpointPath = "/health",
            Status = ModuleEndpointsStatus.PendingConfirmation
        });
        module.SubEndpoints.Add(new ModuleEndpoint
        {
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "POST",
            EndpointPath = "/legacy",
            Status = ModuleEndpointsStatus.PendingConfirmation
        });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var missing = new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/health" };
        var extra = new DiscoveredSubEndpointDto { HttpMethod = "POST", EndpointPath = "/legacy" };
        var partial = new ConfirmEndpointsDto
        {
            ConfirmedEndpoints = confirmMissing || confirmExtra
                ? [new SubEndpointDiscrepancyReportDto
                {
                    ModuleId = module.Id,
                    MissingEndpoints = confirmMissing ? [missing] : null,
                    ExtraEndpoints = confirmExtra ? [extra] : null
                }]
                : []
        };

        var pendingResult = await _sut.ConfirmSubEndpointsAsync(module.Id, partial);

        Assert.Equal(ModuleStatus.PendingConfirmation, pendingResult.Status);
        Assert.Null(pendingResult.ContainerId);
        Assert.Empty(_dockerService.Calls);
        var persisted = await _context.Modules.AsNoTracking().SingleAsync(item => item.Id == module.Id);
        Assert.Equal(ModuleStatus.PendingConfirmation, persisted.Status);
        var endpoints = await _context.ModuleEndpoints.AsNoTracking().Where(item => item.ModuleId == module.Id).ToListAsync();
        Assert.Equal(confirmMissing ? 1 : 2, endpoints.Count);
        Assert.Equal(confirmExtra ? ModuleEndpointsStatus.Active : ModuleEndpointsStatus.PendingConfirmation,
            endpoints.Single(endpoint => endpoint.EndpointPath == "/legacy").Status);

        var confirmedResult = await _sut.ConfirmSubEndpointsAsync(module.Id, new ConfirmEndpointsDto
        {
            ConfirmedEndpoints = [new SubEndpointDiscrepancyReportDto
            {
                ModuleId = module.Id,
                MissingEndpoints = [missing],
                ExtraEndpoints = [extra]
            }]
        });

        Assert.Equal(ModuleStatus.Running, confirmedResult.Status);
        Assert.Equal(["build", "start", "ready"], _dockerService.Calls);
        Assert.DoesNotContain(confirmedResult.Endpoints, endpoint => endpoint.Status == ModuleEndpointsStatus.PendingConfirmation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmSubEndpointsAsync_ProvisioningFails_PersistsFailedStatus(bool failBuild)
    {
        var module = CreateTestModuleEntity("Failed Confirmation", "api/v1/failed-confirmation", 8084);
        module.Status = ModuleStatus.PendingConfirmation;
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
        var failure = new InvalidOperationException("Provisioning failed.");
        if (failBuild)
            _dockerService.BuildException = failure;
        else
            _dockerService.StartException = failure;

        var result = await _sut.ConfirmSubEndpointsAsync(module.Id, new ConfirmEndpointsDto
        {
            ConfirmedEndpoints = [new SubEndpointDiscrepancyReportDto
            {
                ModuleId = module.Id,
                ExtraEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/health" }]
            }]
        });

        Assert.Equal(ModuleStatus.Failed, result.Status);
        Assert.Equal(ModuleEndpointsStatus.Inactive, Assert.Single(result.Endpoints).Status);
        var persisted = await _context.Modules.AsNoTracking().SingleAsync();
        Assert.Equal(ModuleStatus.Failed, persisted.Status);
        Assert.Equal(failBuild ? null : "replacement-container", persisted.ContainerId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmSubEndpointsAsync_SaveFails_CleansCandidateAndPreservesPendingModule(bool cleanupFails, bool cancelSave)
    {
        var module = CreateTestModuleEntity("Unsaved Confirmation", "api/v1/unsaved-confirmation", 8085);
        module.Status = ModuleStatus.PendingConfirmation;
        var currentVersion = module.Versions.Single();
        currentVersion.StoragePath = Path.Combine(_testRootDirectory, "pending-module");
        Directory.CreateDirectory(currentVersion.StoragePath);
        await File.WriteAllTextAsync(Path.Combine(currentVersion.StoragePath, "module.dll"), "original");
        module.SubEndpoints.Add(new ModuleEndpoint
        {
            ModuleId = module.Id,
            ModuleVersion = currentVersion,
            HttpMethod = "GET",
            EndpointPath = "/health",
            Status = ModuleEndpointsStatus.PendingConfirmation
        });
        module.SubEndpoints.Add(new ModuleEndpoint
        {
            ModuleId = module.Id,
            ModuleVersion = currentVersion,
            HttpMethod = "POST",
            EndpointPath = "/legacy",
            Status = ModuleEndpointsStatus.PendingConfirmation
        });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        Exception failure = cancelSave
            ? new OperationCanceledException(cancellation.Token)
            : new InvalidOperationException("Database save failed.");
        _saveInterceptor.ExceptionToThrow = failure;
        if (cancelSave)
            _saveInterceptor.BeforeSave = cancellation.Cancel;
        if (cleanupFails)
            _dockerService.RemoveException = new InvalidOperationException("Cleanup failed.");

        var exception = await Record.ExceptionAsync(() =>
            _sut.ConfirmSubEndpointsAsync(module.Id, new ConfirmEndpointsDto
            {
                ConfirmedEndpoints = [new SubEndpointDiscrepancyReportDto
                {
                    ModuleId = module.Id,
                    MissingEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/health" }],
                    ExtraEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = "POST", EndpointPath = "/legacy" }]
                }]
            }, cancellation.Token));

        Assert.Same(failure, exception);
        Assert.Equal(["build", "start", "ready", "remove:replacement-container"], _dockerService.Calls);
        var persisted = await _context.Modules.AsNoTracking().Include(item => item.SubEndpoints).SingleAsync();
        Assert.Equal(ModuleStatus.PendingConfirmation, persisted.Status);
        Assert.Null(persisted.ContainerId);
        Assert.Equal(2, persisted.SubEndpoints.Count);
        Assert.All(persisted.SubEndpoints, endpoint => Assert.Equal(ModuleEndpointsStatus.PendingConfirmation, endpoint.Status));
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(currentVersion.StoragePath, "module.dll")));

        _saveInterceptor.ExceptionToThrow = null;
        _saveInterceptor.BeforeSave = null;
        await _context.SaveChangesAsync();
        persisted = await _context.Modules.AsNoTracking().Include(item => item.SubEndpoints).SingleAsync();
        Assert.Equal(ModuleStatus.PendingConfirmation, persisted.Status);
        Assert.Null(persisted.ContainerId);
        Assert.Equal(2, persisted.SubEndpoints.Count);
        Assert.All(persisted.SubEndpoints, endpoint => Assert.Equal(ModuleEndpointsStatus.PendingConfirmation, endpoint.Status));
    }
}
