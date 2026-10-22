using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

using Modulix.Database.DbContexts;
using Modulix.Database.Entities;

using Modulix.Docker.Services;
using Modulix.Extensions;
using Modulix.ReverseProxy.Services;
using Modulix.Services;

namespace Modulix.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SubEndpointService"/>.
/// </summary>
[Trait("Category", "Services")]
[Trait("SubCategory", "SubEndpointService")]
public class SubEndpointServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServerContext _context;
    private readonly string _testRootDirectory;
    private readonly FakeDockerService _dockerService;
    private readonly ReverseProxyService _reverseProxyService;
    private readonly FailingSaveInterceptor _saveInterceptor = new();
    private readonly SubEndpointService _sut;

    #region Setup & Teardown

    public SubEndpointServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ServerContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_saveInterceptor)
            .Options;

        _context = new ServerContext(options);
        _context.Database.EnsureCreated();

        _testRootDirectory = Path.Combine(Path.GetTempPath(), "Modulix_SubEndpointTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRootDirectory);

        _dockerService = new FakeDockerService();
        _reverseProxyService = new ReverseProxyService(NullLogger<ReverseProxyService>.Instance);

        _sut = new SubEndpointService(
            _context,
            NullLogger<SubEndpointService>.Instance,
            _dockerService,
            new ModuleEndpointExtension(_context, NullLogger<ModuleEndpointExtension>.Instance, _dockerService, _reverseProxyService));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();

        if (Directory.Exists(_testRootDirectory))
        {
            try
            {
                Directory.Delete(_testRootDirectory, recursive: true);
            }
            catch
            {
                // Ignored during cleanup
            }
        }

        GC.SuppressFinalize(this);
    }

    #endregion

    #region ConfirmSubEndpoints Tests

    [Fact]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmSubEndpointsAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var dto = new ConfirmEndpointsDto();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.ConfirmSubEndpointsAsync(nonExistentId, dto));

        Assert.Equal($"Module with ID '{nonExistentId}' was not found.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmSubEndpointsAsync_ModuleNotInPendingConfirmationStatus_ThrowsInvalidOperationException()
    {
        // Arrange
        var module = CreateTestModuleEntity("Active Module", "api/v1/active", 8080);
        module.Status = ModuleStatus.Created;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var dto = new ConfirmEndpointsDto();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ConfirmSubEndpointsAsync(module.Id, dto));

        Assert.Equal($"Module '{module.Id}' is not in PendingConfirmation status.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmSubEndpointsAsync_ValidDiscrepancies_ActivatesMissingAndRemovesExtraEndpoints()
    {
        // Arrange
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

        // Act
        var result = await _sut.ConfirmSubEndpointsAsync(module.Id, confirmDto);

        // Assert - Return DTO
        Assert.Equal(ModuleStatus.Running, result.Status);

        // Assert - Database state
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
        // Arrange
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

        // Act
        var result = await _sut.ConfirmSubEndpointsAsync(module.Id, confirmDto);

        // Assert
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
        Assert.Equal(ModuleEndpointsStatus.Active, Assert.Single(result.Endpoints).Status);
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

    #endregion

    #region GetModuleSubEndpoints Tests

    [Fact]
    [Trait("Feature", "GetModuleSubEndpoints")]
    public async Task GetModuleSubEndpointsAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.GetModuleSubEndpointsAsync(nonExistentId));

        Assert.Equal($"Module with ID '{nonExistentId}' was not found.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "GetModuleSubEndpoints")]
    public async Task GetModuleSubEndpointsAsync_ModuleExists_ReturnsAllSubEndpoints()
    {
        // Arrange
        var module = CreateTestModuleEntity("Gateway", "api/v1/gw", 8004);
        var ep1 = new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = module.Id, ModuleVersion = module.Versions.Single(), HttpMethod = "GET", EndpointPath = "/a" };
        var ep2 = new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = module.Id, ModuleVersion = module.Versions.Single(), HttpMethod = "POST", EndpointPath = "/b" };

        _context.Modules.Add(module);
        _context.ModuleEndpoints.AddRange(ep1, ep2);
        await _context.SaveChangesAsync();

        // Act
        var pagedResult = await _sut.GetModuleSubEndpointsAsync(module.Id);
        var result = pagedResult.Items.ToList();

        // Assert
        Assert.Equal(2, pagedResult.TotalCount);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.EndpointPath == "/a" && e.HttpMethod == "GET");
        Assert.Contains(result, e => e.EndpointPath == "/b" && e.HttpMethod == "POST");
    }

    [Fact]
    [Trait("Feature", "GetModuleSubEndpoints")]
    public async Task GetModuleSubEndpointsAsync_FiltersBeforePaging_ReturnsOnlyTargetModuleEndpoints()
    {
        // Arrange
        var otherModule = CreateTestModuleEntity("Other", "api/v1/other", 8005);
        var targetModule = CreateTestModuleEntity("Target", "api/v1/target", 8006);
        _context.Modules.AddRange(otherModule, targetModule);
        _context.ModuleEndpoints.AddRange(
            new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = otherModule.Id, ModuleVersion = otherModule.Versions.Single(), HttpMethod = "GET", EndpointPath = "/other-1" },
            new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = otherModule.Id, ModuleVersion = otherModule.Versions.Single(), HttpMethod = "GET", EndpointPath = "/other-2" },
            new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = targetModule.Id, ModuleVersion = targetModule.Versions.Single(), HttpMethod = "GET", EndpointPath = "/target-1" },
            new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = targetModule.Id, ModuleVersion = targetModule.Versions.Single(), HttpMethod = "POST", EndpointPath = "/target-2" });
        await _context.SaveChangesAsync();

        // Act
        var page1 = await _sut.GetModuleSubEndpointsAsync(targetModule.Id, new PaginationQueryDto { Page = 1, PageSize = 1 });
        var page2 = await _sut.GetModuleSubEndpointsAsync(targetModule.Id, new PaginationQueryDto { Page = 2, PageSize = 1 });

        // Assert
        Assert.Equal(2, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Single(page1.Items);
        Assert.True(page1.HasNextPage);
        Assert.Single(page2.Items);
        Assert.True(page2.HasPreviousPage);
        Assert.NotEqual(page1.Items[0].EndpointPath, page2.Items[0].EndpointPath);
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

    private sealed class FakeDockerService : IDockerService
    {
        public List<string> RemovedContainerIds { get; } = [];
        public List<string> Calls { get; } = [];
        public Exception? BuildException { get; set; }
        public Exception? StartException { get; set; }
        public Exception? ReadinessException { get; set; }
        public Exception? RemoveException { get; set; }

        public Task<string> BuildContainerAsync(Guid moduleId, string storagePath, string entryDllName, int containerPort, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add("build");
            if (BuildException is not null)
                throw BuildException;
            return Task.FromResult("replacement-container");
        }

        public Task RunContainerAsync(string containerId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add("start");
            if (StartException is not null)
                throw StartException;
            return Task.CompletedTask;
        }

        public Task WaitUntilReadyAsync(string containerId, CancellationToken ct = default)
        {
            Calls.Add("ready");
            ct.ThrowIfCancellationRequested();
            if (ReadinessException is not null)
                throw ReadinessException;
            return Task.CompletedTask;
        }

        public Task StopContainerAsync(string containerId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add("stop:" + containerId);
            return Task.CompletedTask;
        }

        public Task RemoveContainerAsync(string containerId, Guid moduleId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add("remove:" + containerId);
            RemovedContainerIds.Add(containerId);
            if (RemoveException is not null)
                throw RemoveException;
            return Task.CompletedTask;
        }

        public async Task<bool> TryRemoveModuleContainerAsync(string containerId, Guid moduleId)
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await RemoveContainerAsync(containerId, moduleId, cleanupCts.Token);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public Task<string> GetContainerLogsAsync(string containerId, int? tail = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public Exception? ExceptionToThrow { get; set; }
        public Action? BeforeSave { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            BeforeSave?.Invoke();
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return ValueTask.FromResult(result);
        }
    }

    #endregion
}
