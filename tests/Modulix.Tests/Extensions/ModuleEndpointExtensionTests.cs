using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Database.DbContexts;
using Modulix.Database.Entities;
using Modulix.Extensions;
using Modulix.Models.Dtos;
using Modulix.Models.Enums;
using Modulix.Services.Interfaces;

namespace Modulix.Tests.Extensions;

[Trait("Category", "Extensions")]
[Trait("SubCategory", "ModuleEndpointExtension")]
public class ModuleEndpointExtensionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServerContext _context;
    private readonly RecordingDockerService _dockerService = new();
    private readonly ModuleEndpointExtension _sut;

    #region Setup & Teardown

    public ModuleEndpointExtensionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new ServerContext(new DbContextOptionsBuilder<ServerContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _sut = new ModuleEndpointExtension(_context, NullLogger<ModuleEndpointExtension>.Instance, _dockerService);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion

    #region CheckBaseEndpointPathConflict Tests

    [Theory]
    [InlineData("api/modules", "api/modules")]
    [InlineData("api/modules/items", "api/modules")]
    [InlineData("api/modules", "api/modules/items")]
    public void CheckBaseEndpointPathConflict_EqualOrNestedRoutes_Throws(string usedPath, string requestedPath)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ModuleEndpointExtension.CheckBaseEndpointPathConflict([usedPath], requestedPath));

        Assert.Contains(requestedPath, exception.Message);
    }

    [Fact]
    public void CheckBaseEndpointPathConflict_DistinctRoutes_DoesNotRejectSiblingModule()
    {
        ModuleEndpointExtension.CheckBaseEndpointPathConflict(["api/orders", "api/products"], "api/customers");
    }

    #endregion

    #region CreateEndpointDiscrepancyReport Tests

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateEndpointDiscrepancyReportAsync_NoManualEndpoints_ImportsRoutesAndStartsHealthyContainer(bool discoversEndpoints)
    {
        var module = await CreateModuleAsync();
        var discovered = discoversEndpoints
            ? new List<DiscoveredEndpointDto> { new() { HttpMethod = "GET", EndpointPath = "/health" } }
            : [];

        var report = await _sut.CreateEndpointDiscrepancyReportAsync(module, discovered);

        Assert.Null(report);
        var persisted = await LoadPersistedModuleAsync(module.Id);
        Assert.Equal(ModuleStatus.Running, persisted.Status);
        Assert.Equal("candidate", persisted.ContainerId);
        Assert.Equal(new[] { "build", "start:candidate", "ready:candidate" }, _dockerService.Calls);
        Assert.Equal((module.Id, module.StoragePath, "module.dll", 8080), _dockerService.BuildArguments);
        if (discoversEndpoints)
        {
            var endpoint = Assert.Single(persisted.SubEndpoints);
            Assert.Equal(module.Id, endpoint.ModuleId);
            Assert.Equal("GET", endpoint.HttpMethod);
            Assert.Equal("/health", endpoint.EndpointPath);
            Assert.Equal(ModuleEndpointsStatus.Active, endpoint.Status);
        }
        else
            Assert.Empty(persisted.SubEndpoints);
    }

    [Fact]
    public async Task CreateEndpointDiscrepancyReportAsync_AllRoutesMatchIgnoringCase_ActivatesWithoutDuplicatesAndWaitsForReadiness()
    {
        var module = await CreateModuleAsync(new CreateModuleEndpointDto { HttpMethod = "GET", EndpointPath = "/Items" });
        var endpointId = Assert.Single(module.SubEndpoints).Id;

        var report = await _sut.CreateEndpointDiscrepancyReportAsync(module,
            [new DiscoveredEndpointDto { HttpMethod = "get", EndpointPath = "/items" }]);

        Assert.Null(report);
        var persisted = await LoadPersistedModuleAsync(module.Id);
        Assert.Equal(ModuleStatus.Running, persisted.Status);
        var endpoint = Assert.Single(persisted.SubEndpoints);
        Assert.Equal(endpointId, endpoint.Id);
        Assert.Equal(ModuleEndpointsStatus.Active, endpoint.Status);
        Assert.Equal(new[] { "build", "start:candidate", "ready:candidate" }, _dockerService.Calls);
    }

    [Fact]
    public async Task CreateEndpointDiscrepancyReportAsync_MixedRoutes_PersistsDiscrepanciesWithoutStartingContainer()
    {
        var module = await CreateModuleAsync(
            new CreateModuleEndpointDto { HttpMethod = "GET", EndpointPath = "/items" },
            new CreateModuleEndpointDto { HttpMethod = "DELETE", EndpointPath = "/old" });

        var report = await _sut.CreateEndpointDiscrepancyReportAsync(module,
        [
            new DiscoveredEndpointDto { HttpMethod = "GET", EndpointPath = "/items" },
            new DiscoveredEndpointDto { HttpMethod = "POST", EndpointPath = "/new" }
        ]);

        Assert.NotNull(report);
        Assert.Equal(module.Id, report.ModuleId);
        Assert.True(report.HasDiscrepancy);
        Assert.NotNull(report.MatchedEndpoints);
        Assert.NotNull(report.ExtraEndpoints);
        Assert.NotNull(report.MissingEndpoints);
        Assert.Equal("/items", Assert.Single(report.MatchedEndpoints).EndpointPath);
        Assert.Equal("/new", Assert.Single(report.ExtraEndpoints).EndpointPath);
        Assert.Equal("/old", Assert.Single(report.MissingEndpoints).EndpointPath);
        Assert.Empty(_dockerService.Calls);
        var persisted = await LoadPersistedModuleAsync(module.Id);
        Assert.Equal(ModuleStatus.PendingConfirmation, persisted.Status);
        Assert.Null(persisted.ContainerId);
        Assert.Equal(3, persisted.SubEndpoints.Count);
        Assert.Equal(ModuleEndpointsStatus.Active, Assert.Single(persisted.SubEndpoints, endpoint => endpoint.EndpointPath == "/items").Status);
        Assert.All(persisted.SubEndpoints.Where(endpoint => endpoint.EndpointPath != "/items"),
            endpoint => Assert.Equal(ModuleEndpointsStatus.PendingConfirmation, endpoint.Status));
    }

    [Theory]
    [InlineData("build", 1)]
    [InlineData("start:candidate", 2)]
    [InlineData("ready:candidate", 3)]
    public async Task CreateEndpointDiscrepancyReportAsync_ProvisioningFails_PersistsFailedStatusAndStopsNextSteps(string failingOperation, int expectedCalls)
    {
        var module = await CreateModuleAsync();
        _dockerService.FailingOperation = failingOperation;

        var report = await _sut.CreateEndpointDiscrepancyReportAsync(module, []);

        Assert.Null(report);
        var persisted = await LoadPersistedModuleAsync(module.Id);
        Assert.Equal(ModuleStatus.Failed, persisted.Status);
        Assert.Equal(expectedCalls, _dockerService.Calls.Count);
        Assert.Equal(failingOperation, _dockerService.Calls.Last());
        Assert.Equal(failingOperation == "build" ? null : "candidate", persisted.ContainerId);
    }

    #endregion

    #region Test Fakes & Helpers

    private async Task<Module> CreateModuleAsync(params CreateModuleEndpointDto[] endpoints)
    {
        var module = new Module
        {
            ModuleName = "Test module",
            BaseEndpointPath = "api/module",
            ContainerPort = 8080,
            StoragePath = "/test/module",
            ModuleEntryAssemblyFileName = "module.dll",
            Status = ModuleStatus.Created
        };
        foreach (var endpoint in endpoints)
            module.SubEndpoints.Add(new ModuleEndpoint
            {
                ModuleId = module.Id,
                HttpMethod = endpoint.HttpMethod,
                EndpointPath = endpoint.EndpointPath,
                Status = ModuleEndpointsStatus.PendingConfirmation
            });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        return module;
    }

    private Task<Module> LoadPersistedModuleAsync(Guid moduleId) =>
        _context.Modules.AsNoTracking().Include(module => module.SubEndpoints).SingleAsync(module => module.Id == moduleId);

    private sealed class RecordingDockerService : IDockerService
    {
        public List<string> Calls { get; } = [];
        public string? FailingOperation { get; set; }
        public (Guid ModuleId, string StoragePath, string EntryAssembly, int Port) BuildArguments { get; private set; }

        public Task<string> BuildContainerAsync(Guid moduleId, string storagePath, string entryDllName, int containerPort, CancellationToken ct = default)
        {
            BuildArguments = (moduleId, storagePath, entryDllName, containerPort);
            Record("build", ct);
            return Task.FromResult("candidate");
        }

        public Task RunContainerAsync(string containerId, CancellationToken ct = default)
        {
            Record("start:" + containerId, ct);
            return Task.CompletedTask;
        }

        public Task WaitUntilReadyAsync(string containerId, CancellationToken ct = default)
        {
            Record("ready:" + containerId, ct);
            return Task.CompletedTask;
        }

        public Task StopContainerAsync(string containerId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveContainerAsync(string containerId, Guid moduleId, CancellationToken ct = default) => throw new NotSupportedException();

        private void Record(string operation, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add(operation);
            if (FailingOperation == operation)
                throw new InvalidOperationException("Docker operation failed: " + operation);
        }
    }

    #endregion
}