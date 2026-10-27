using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;
using Modulix.Tests.TestData;

namespace Modulix.Tests.Services;

/// <summary>
/// Module creation and initial archive scanning tests for <see cref="Modulix.Services.ModuleService"/>.
/// </summary>
public partial class ModuleServiceTests
{
    [Theory]
    [ClassData(typeof(InvalidBaseUrlPathsTestData))]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_InvalidBaseUrlPath_ThrowsArgumentException(string? invalidPath)
    {
        var dto = new CreateModuleDto
        {
            ModuleName = "Test Module",
            BaseUrlPath = invalidPath!,
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile()
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateModuleAsync(dto));

        Assert.Equal("Base endpoint path cannot be empty.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ContainerPortAlreadyInUse_ThrowsArgumentException()
    {
        var existingModule = CreateTestModuleEntity("Existing", "api/v1/existing", 8080);
        _context.Modules.Add(existingModule);
        await _context.SaveChangesAsync();

        var dto = new CreateModuleDto
        {
            ModuleName = "New Module",
            BaseUrlPath = "api/v1/new",
            ContainerPort = 8080,
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile()
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateModuleAsync(dto));

        Assert.Contains("Container port '8080' is already in use.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_NoPortProvided_AssignsFirstAvailablePort()
    {
        var existingModule = CreateTestModuleEntity("Existing", "api/v1/existing", 1024);
        _context.Modules.Add(existingModule);
        await _context.SaveChangesAsync();

        var dto = new CreateModuleDto
        {
            ModuleName = "Auto Port Module",
            BaseUrlPath = "api/v1/autoport",
            ContainerPort = null,
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile()
        };

        var result = await _sut.CreateModuleAsync(dto);

        Assert.NotNull(result);
        Assert.Equal(1025, result.Module.ContainerPort);

        var persisted = await _context.Modules.FindAsync(result.Module.Id);
        Assert.NotNull(persisted);
        Assert.Equal(1025, persisted.ContainerPort);
    }

    [Theory]
    [ClassData(typeof(ConflictingBaseUrlPathsTestData))]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ConflictingBasePath_ThrowsArgumentException(string existingPath, string newPath)
    {
        var existingModule = CreateTestModuleEntity("Existing", existingPath, 8080);
        _context.Modules.Add(existingModule);
        await _context.SaveChangesAsync();

        var dto = new CreateModuleDto
        {
            ModuleName = "Conflicting Module",
            BaseUrlPath = newPath,
            ContainerPort = 8081,
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile()
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateModuleAsync(dto));
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_EmptyArchiveFile_ThrowsArgumentException()
    {
        var emptyFile = new FormFile(new MemoryStream(), 0, 0, "ModuleFile", "module.zip");
        var dto = new CreateModuleDto
        {
            ModuleName = "Empty File Module",
            BaseUrlPath = "api/v1/empty",
            Version = "v1.0.0",
            ModuleFile = emptyFile
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateModuleAsync(dto));

        Assert.Equal("Module file cannot be empty.", exception.Message);
    }

    [Theory]
    [ClassData(typeof(InvalidModuleArchiveExtensionsTestData))]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_NonZipArchive_ThrowsArgumentException(string fileName)
    {
        var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes("arbitrary content"));
        var invalidFile = new FormFile(memoryStream, 0, memoryStream.Length, "ModuleFile", fileName);

        var dto = new CreateModuleDto
        {
            ModuleName = "Invalid Ext Module",
            BaseUrlPath = "api/v1/invalidext",
            Version = "v1.0.0",
            ModuleFile = invalidFile
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateModuleAsync(dto));

        Assert.Equal("Module file must be a .zip archive.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ValidInput_ExtractsFilesAndPersistsModule()
    {
        const string entryName = "service.dll";
        var zipFile = CreateDummyZipFile("upload.zip", entryName);

        var dto = new CreateModuleDto
        {
            ModuleName = "Valid Analytics",
            Description = "Processes real-time analytics",
            BaseUrlPath = "/api/v1/analytics/",
            ContainerPort = 9000,
            Version = "v1.0.0",
            ModuleFile = zipFile
        };

        var result = await _sut.CreateModuleAsync(dto);

        Assert.NotNull(result);
        Assert.Equal("Valid Analytics", result.Module.ModuleName);
        Assert.Equal("api/v1/analytics", result.Module.BaseUrlPath);
        Assert.Equal(9000, result.Module.ContainerPort);
        var createdVersion = Assert.Single(result.Module.Versions);
        Assert.True(Directory.Exists(createdVersion.StoragePath));
        Assert.True(File.Exists(Path.Combine(createdVersion.StoragePath, entryName)));
        Assert.Equal(createdVersion.StoragePath, _endpointScanner.ScannedDirectory);
        Assert.False(result.DiscrepancyReport!.HasDiscrepancy);
        Assert.Equal(ModuleStatus.Running, result.Module.Status);

        var persisted = await _context.Modules.Include(m => m.Versions).SingleAsync(m => m.Id == result.Module.Id);
        Assert.NotNull(persisted);
        Assert.Equal("api/v1/analytics", persisted.BaseUrlPath);
        Assert.Equal(createdVersion.StoragePath, persisted.Versions.Single().StoragePath);
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_NoInitialEndpoints_PersistsAllDiscoveredEndpoints()
    {
        _endpointScanner.DiscoveredEndpoints =
        [
            new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/health" },
            new DiscoveredSubEndpointDto { HttpMethod = "POST", EndpointPath = "/jobs" }
        ];

        var dto = new CreateModuleDto
        {
            ModuleName = "Scanned Module",
            BaseUrlPath = "api/v1/scanned",
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile()
        };

        var result = await _sut.CreateModuleAsync(dto);

        var persistedEndpoints = await _context.ModuleEndpoints
            .Where(endpoint => endpoint.ModuleId == result.Module.Id)
            .ToListAsync();

        Assert.Equal(2, persistedEndpoints.Count);
        Assert.All(persistedEndpoints, endpoint => Assert.Equal(ModuleEndpointsStatus.Active, endpoint.Status));
        Assert.Contains(persistedEndpoints, endpoint => endpoint.HttpMethod == "GET" && endpoint.EndpointPath == "/health");
        Assert.Contains(persistedEndpoints, endpoint => endpoint.HttpMethod == "POST" && endpoint.EndpointPath == "/jobs");
        Assert.False(result.DiscrepancyReport!.HasDiscrepancy);
        Assert.Equal(ModuleStatus.Running, result.Module.Status);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("absolute")]
    [InlineData("relative")]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ClientFileName_DoesNotOverwriteOrDeleteExistingFile(string pathKind)
    {
        var existingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        await File.WriteAllTextAsync(existingPath, "existing file");
        var fileName = pathKind switch
        {
            "absolute" => existingPath,
            "relative" => Path.Combine("..", Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetTempPath())), Path.GetFileName(existingPath)),
            _ => Path.GetFileName(existingPath)
        };

        try
        {
            var result = await _sut.CreateModuleAsync(new CreateModuleDto
            {
                ModuleName = "Safe Upload",
                BaseUrlPath = "api/v1/safe-upload",
                Version = "v1.0.0",
                ModuleFile = CreateDummyZipFile(fileName)
            });

            Assert.Equal(ModuleStatus.Running, result.Module.Status);
            Assert.Equal("existing file", await File.ReadAllTextAsync(existingPath));
        }
        finally
        {
            File.Delete(existingPath);
        }
    }

    [Theory]
    [InlineData("running", false)]
    [InlineData("running", true)]
    [InlineData("pending", false)]
    [InlineData("build", false)]
    [InlineData("start", false)]
    [InlineData("canceled", false)]
    [InlineData("canceled", true)]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_SaveFails_CleansCandidateAndPreservesOriginalFailure(string scenario, bool cleanupFails)
    {
        using var cancellation = new CancellationTokenSource();
        Exception failure = scenario == "canceled"
            ? new OperationCanceledException(cancellation.Token)
            : new InvalidOperationException("Database save failed.");
        _saveInterceptor.ExceptionToThrow = failure;
        if (scenario == "canceled")
            _saveInterceptor.BeforeSave = cancellation.Cancel;
        if (cleanupFails)
            _dockerService.RemoveException = new InvalidOperationException("Cleanup failed.");
        if (scenario == "build")
            _dockerService.BuildException = new InvalidOperationException("Build failed.");
        if (scenario == "start")
            _dockerService.StartException = new InvalidOperationException("Start failed.");
        _endpointScanner.DiscoveredEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/health" }];

        var exception = await Record.ExceptionAsync(() =>
            _sut.CreateModuleAsync(new CreateModuleDto
            {
                ModuleName = "Unsaved Module",
                BaseUrlPath = "api/v1/unsaved",
                Version = "v1.0.0",
                ModuleFile = CreateDummyZipFile(),
                InitialEndpoints = scenario == "pending"
                    ? [new CreateModuleSubEndpointDto { HttpMethod = "POST", EndpointPath = "/jobs" }]
                    : null
            }, cancellation.Token));

        Assert.Same(failure, exception);
        var expectedCalls = scenario switch
        {
            "pending" => Array.Empty<string>(),
            "build" => ["build"],
            "start" => ["build", "start", "remove:replacement-container"],
            _ => ["build", "start", "ready", "remove:replacement-container"]
        };
        Assert.Equal(expectedCalls, _dockerService.Calls);
        Assert.Empty(await _context.Modules.AsNoTracking().ToListAsync());
        Assert.Empty(await _context.ModuleEndpoints.AsNoTracking().ToListAsync());
        Assert.Equal(cleanupFails, Directory.Exists(_endpointScanner.ScannedDirectory));

        _saveInterceptor.ExceptionToThrow = null;
        _saveInterceptor.BeforeSave = null;
        await _context.SaveChangesAsync();
        Assert.Empty(await _context.Modules.AsNoTracking().ToListAsync());
        Assert.Empty(await _context.ModuleEndpoints.AsNoTracking().ToListAsync());
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ScannerQueuesWork_PersistsPendingModuleWithoutProvisioning()
    {
        _endpointScanner.WasQueued = true;

        var result = await _sut.CreateModuleAsync(new CreateModuleDto
        {
            ModuleName = "Queued module",
            BaseUrlPath = "api/queued",
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile(),
            InitialEndpoints = [new CreateModuleSubEndpointDto { HttpMethod = "GET", EndpointPath = "/health" }]
        });

        Assert.Equal(ModuleStatus.QueuedForScan, result.Module.Status);
        Assert.Null(result.DiscrepancyReport);
        Assert.Empty(_dockerService.Calls);
        Assert.Empty(_endpointScanner.CancelledModuleIds);
        var persisted = await _context.Modules.AsNoTracking().Include(module => module.SubEndpoints).Include(module => module.Versions).SingleAsync();
        Assert.Equal(result.Module.Id, persisted.Id);
        Assert.Equal(ModuleStatus.QueuedForScan, persisted.Status);
        Assert.Null(persisted.ContainerId);
        Assert.Equal(string.Empty, persisted.Versions.Single().ModuleEntryAssemblyFileName);
        Assert.Equal(ModuleEndpointsStatus.PendingConfirmation, Assert.Single(persisted.SubEndpoints).Status);
        Assert.True(Directory.Exists(persisted.Versions.Single().StoragePath));
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_QueuedModuleSaveFails_CancelsScanAndRemovesUnpersistedFiles()
    {
        _endpointScanner.WasQueued = true;
        var failure = new InvalidOperationException("Queue persistence failed.");
        _saveInterceptor.ExceptionToThrow = failure;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateModuleAsync(new CreateModuleDto
        {
            ModuleName = "Queued module",
            BaseUrlPath = "api/queued",
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile()
        }));

        Assert.Same(failure, exception);
        Assert.Single(_endpointScanner.CancelledModuleIds);
        Assert.False(Directory.Exists(_endpointScanner.ScannedDirectory));
        Assert.Empty(_dockerService.Calls);
        _saveInterceptor.ExceptionToThrow = null;
        await _context.SaveChangesAsync();
        Assert.Empty(await _context.Modules.AsNoTracking().ToListAsync());
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_AllInitialEndpointsMatch_ReturnsMatchedReportAndCreatedModule()
    {
        _endpointScanner.DiscoveredEndpoints =
        [
            new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/health" }
        ];

        var dto = new CreateModuleDto
        {
            ModuleName = "Matched Module",
            BaseUrlPath = "api/v1/matched",
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile(),
            InitialEndpoints =
            [
                new CreateModuleSubEndpointDto { HttpMethod = "GET", EndpointPath = "/health" }
            ]
        };

        var result = await _sut.CreateModuleAsync(dto);

        Assert.False(result.DiscrepancyReport!.HasDiscrepancy);
        Assert.Equal(ModuleStatus.Running, result.Module.Status);
        Assert.Single(result.DiscrepancyReport.MatchedEndpoints!);
        Assert.Null(result.DiscrepancyReport.MissingEndpoints);
        Assert.Null(result.DiscrepancyReport.ExtraEndpoints);
    }

    [Theory]
    [InlineData("health")]
    [InlineData("  api/v1/items  ")]
    [InlineData("/api/v1/items")]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_InitialEndpointsWithoutLeadingSlash_NormalizesEndpointPaths(string rawEndpointPath)
    {
        var expectedNormalizedPath = "/" + rawEndpointPath.Trim().TrimStart('/');
        _endpointScanner.DiscoveredEndpoints =
        [
            new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = expectedNormalizedPath }
        ];

        var dto = new CreateModuleDto
        {
            ModuleName = "Normalized Endpoints Module",
            BaseUrlPath = "api/v1/normalized",
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile(),
            InitialEndpoints =
            [
                new CreateModuleSubEndpointDto { HttpMethod = "GET", EndpointPath = rawEndpointPath }
            ]
        };

        var result = await _sut.CreateModuleAsync(dto);

        Assert.False(result.DiscrepancyReport!.HasDiscrepancy);
        Assert.Equal(ModuleStatus.Running, result.Module.Status);
        var matched = Assert.Single(result.DiscrepancyReport.MatchedEndpoints!);
        Assert.Equal(expectedNormalizedPath, matched.EndpointPath);

        var persistedEndpoint = await _context.ModuleEndpoints
            .SingleAsync(endpoint => endpoint.ModuleId == result.Module.Id);
        Assert.Equal(expectedNormalizedPath, persistedEndpoint.EndpointPath);
        Assert.Equal(ModuleEndpointsStatus.Active, persistedEndpoint.Status);
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ScannerFails_DoesNotPersistModuleOrLeaveStorageDirectory()
    {
        _endpointScanner.ExceptionToThrow = new InvalidOperationException("Module scan failed.");
        var dto = new CreateModuleDto
        {
            ModuleName = "Invalid Module",
            BaseUrlPath = "api/v1/invalid",
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile()
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.CreateModuleAsync(dto));

        Assert.Equal("Module scan failed.", exception.Message);
        Assert.Empty(await _context.Modules.ToListAsync());
        AssertNoStoredModuleDirectories();
    }

    [Theory]
    [InlineData(true, "Module archive exceeds the maximum allowed number of entries.")]
    [InlineData(false, "Module archive exceeds the maximum allowed uncompressed size.")]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ArchiveExceedsLimits_DoesNotPersistModuleOrLeaveStorageDirectory(
        bool exceedsEntryCount, string expectedMessage)
    {
        var dto = new CreateModuleDto
        {
            ModuleName = "Oversized Archive Module",
            BaseUrlPath = "api/v1/oversized-archive",
            Version = "v1.0.0",
            ModuleFile = exceedsEntryCount
                ? CreateZipFileWithEntries(1_001)
                : CreateZipFileWithClaimedUncompressedSize(512U * 1024 * 1024 + 1)
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.CreateModuleAsync(dto));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.Empty(await _context.Modules.ToListAsync());
        AssertNoStoredModuleDirectories();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ProvisioningFails_PersistsFailedStatus(bool failBuild)
    {
        var failure = new InvalidOperationException("Provisioning failed.");
        if (failBuild)
            _dockerService.BuildException = failure;
        else
            _dockerService.StartException = failure;

        var result = await _sut.CreateModuleAsync(new CreateModuleDto
        {
            ModuleName = "Failed Module",
            BaseUrlPath = "api/v1/failed",
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile()
        });

        Assert.Equal(ModuleStatus.Failed, result.Module.Status);
        Assert.Equal(failBuild ? null : "replacement-container", result.Module.ContainerId);
        var persisted = await _context.Modules.AsNoTracking().SingleAsync();
        Assert.Equal(result.Module.Id, persisted.Id);
        Assert.Equal(ModuleStatus.Failed, persisted.Status);
        Assert.Equal(failBuild ? ["build"] : new[] { "build", "start" }, _dockerService.Calls);
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ReconcilesInitialAndDiscoveredEndpoints()
    {
        _endpointScanner.DiscoveredEndpoints =
        [
            new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/health" },
            new DiscoveredSubEndpointDto { HttpMethod = "DELETE", EndpointPath = "/legacy" }
        ];

        var dto = new CreateModuleDto
        {
            ModuleName = "Reconciled Module",
            BaseUrlPath = "api/v1/reconciled",
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile(),
            InitialEndpoints =
            [
                new CreateModuleSubEndpointDto { HttpMethod = "GET", EndpointPath = "/health" },
                new CreateModuleSubEndpointDto { HttpMethod = "POST", EndpointPath = "/jobs" }
            ]
        };

        var result = await _sut.CreateModuleAsync(dto);

        Assert.True(result.DiscrepancyReport!.HasDiscrepancy);
        Assert.Equal(ModuleStatus.PendingConfirmation, result.Module.Status);
        Assert.Empty(_dockerService.Calls);
        Assert.Contains(result.DiscrepancyReport.MatchedEndpoints!, endpoint => endpoint.HttpMethod == "GET" && endpoint.EndpointPath == "/health");
        Assert.Contains(result.DiscrepancyReport.MissingEndpoints!, endpoint => endpoint.HttpMethod == "POST" && endpoint.EndpointPath == "/jobs");
        Assert.Contains(result.DiscrepancyReport.ExtraEndpoints!, endpoint => endpoint.HttpMethod == "DELETE" && endpoint.EndpointPath == "/legacy");

        var persistedEndpoints = await _context.ModuleEndpoints
            .Where(endpoint => endpoint.ModuleId == result.Module.Id)
            .ToListAsync();

        Assert.Contains(persistedEndpoints, endpoint =>
            endpoint.HttpMethod == "GET" && endpoint.EndpointPath == "/health" && endpoint.Status == ModuleEndpointsStatus.Active);
        Assert.Contains(persistedEndpoints, endpoint =>
            endpoint.HttpMethod == "POST" && endpoint.EndpointPath == "/jobs" && endpoint.Status == ModuleEndpointsStatus.PendingConfirmation);
        Assert.Contains(persistedEndpoints, endpoint =>
            endpoint.HttpMethod == "DELETE" && endpoint.EndpointPath == "/legacy" && endpoint.Status == ModuleEndpointsStatus.PendingConfirmation);
    }
}
