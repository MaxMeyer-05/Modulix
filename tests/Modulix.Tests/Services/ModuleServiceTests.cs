using System.Text;
using System.Buffers.Binary;
using System.IO.Compression;

using Microsoft.Data.Sqlite;

using Microsoft.AspNetCore.Http;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Database.Entities;
using Modulix.Database.DbContexts;

using Modulix.Extensions;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

using Modulix.Models.Options;

using Modulix.Docker.Services;
using Modulix.ReverseProxy.Services;
using Modulix.Scanner.Interfaces;
using Modulix.Services;
using Modulix.Services.Interfaces;

using Modulix.Tests.TestData;

namespace Modulix.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ModuleService"/>.
/// </summary>
[Trait("Category", "Services")]
[Trait("SubCategory", "ModuleService")]
public class ModuleServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServerContext _context;
    private readonly string _testRootDirectory;
    private readonly FakeModuleEndpointScanner _endpointScanner;
    private readonly FakeDockerService _dockerService;
    private readonly ReverseProxyService _reverseProxyService;
    private readonly FailingSaveInterceptor _saveInterceptor = new();
    private readonly SubEndpointService _subEndpointService;
    private readonly ScannerService _scannerService;
    private readonly ModuleVersionService _moduleVersionService;
    private readonly ModuleService _sut;

    #region Setup & Teardown

    public ModuleServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ServerContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_saveInterceptor)
            .Options;

        _context = new ServerContext(options);
        _context.Database.EnsureCreated();

        _testRootDirectory = Path.Combine(Path.GetTempPath(), "Modulix_Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRootDirectory);

        var moduleOptions = Options.Create(new ModulixOptions
        {
            StorageBasePath = Path.Combine(_testRootDirectory, "storage", "modules")
        });
        _endpointScanner = new FakeModuleEndpointScanner();
        _dockerService = new FakeDockerService();
        _reverseProxyService = new ReverseProxyService(NullLogger<ReverseProxyService>.Instance);
        var directoryExtension = new DirectoryExtension(NullLogger<DirectoryExtension>.Instance, moduleOptions);
        var moduleEndpointExtension = new ModuleEndpointExtension(
            _context,
            NullLogger<ModuleEndpointExtension>.Instance,
            _dockerService,
            _reverseProxyService);

        _subEndpointService = new SubEndpointService(
            _context,
            NullLogger<SubEndpointService>.Instance,
            _dockerService,
            moduleEndpointExtension);
        _scannerService = new ScannerService(
            _context,
            NullLogger<ScannerService>.Instance,
            _endpointScanner,
            moduleEndpointExtension);
        _moduleVersionService = new ModuleVersionService(
            _context,
            NullLogger<ModuleVersionService>.Instance,
            _dockerService,
            _endpointScanner,
            directoryExtension,
            moduleEndpointExtension);

        _sut = new ModuleService(
            _context,
            NullLogger<ModuleService>.Instance,
            moduleOptions,
            _dockerService,
            _endpointScanner,
            directoryExtension,
            moduleEndpointExtension,
            _reverseProxyService);
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

    #region CreateModule Tests

    [Theory]
    [ClassData(typeof(InvalidBaseUrlPathsTestData))]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_InvalidBaseUrlPath_ThrowsArgumentException(string? invalidPath)
    {
        // Arrange
        var dto = new CreateModuleDto
        {
            ModuleName = "Test Module",
            BaseUrlPath = invalidPath!,
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile()
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateModuleAsync(dto));

        Assert.Equal("Base endpoint path cannot be empty.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ContainerPortAlreadyInUse_ThrowsArgumentException()
    {
        // Arrange
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

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateModuleAsync(dto));

        Assert.Contains("Container port '8080' is already in use.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_NoPortProvided_AssignsFirstAvailablePort()
    {
        // Arrange
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

        // Act
        var result = await _sut.CreateModuleAsync(dto);

        // Assert
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
        // Arrange
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

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateModuleAsync(dto));
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_EmptyArchiveFile_ThrowsArgumentException()
    {
        // Arrange
        var emptyFile = new FormFile(new MemoryStream(), 0, 0, "ModuleFile", "module.zip");
        var dto = new CreateModuleDto
        {
            ModuleName = "Empty File Module",
            BaseUrlPath = "api/v1/empty",
            Version = "v1.0.0",
            ModuleFile = emptyFile
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateModuleAsync(dto));

        Assert.Equal("Module file cannot be empty.", exception.Message);
    }

    [Theory]
    [ClassData(typeof(InvalidModuleArchiveExtensionsTestData))]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_NonZipArchive_ThrowsArgumentException(string fileName)
    {
        // Arrange
        var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes("arbitrary content"));
        var invalidFile = new FormFile(memoryStream, 0, memoryStream.Length, "ModuleFile", fileName);

        var dto = new CreateModuleDto
        {
            ModuleName = "Invalid Ext Module",
            BaseUrlPath = "api/v1/invalidext",
            Version = "v1.0.0",
            ModuleFile = invalidFile
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateModuleAsync(dto));

        Assert.Equal("Module file must be a .zip archive.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ValidInput_ExtractsFilesAndPersistsModule()
    {
        // Arrange
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

        // Act
        var result = await _sut.CreateModuleAsync(dto);

        // Assert - Return DTO
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

        // Assert - Database state
        var persisted = await _context.Modules.Include(m => m.Versions).SingleAsync(m => m.Id == result.Module.Id);
        Assert.NotNull(persisted);
        Assert.Equal("api/v1/analytics", persisted.BaseUrlPath);
        Assert.Equal(createdVersion.StoragePath, persisted.Versions.Single().StoragePath);
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_NoInitialEndpoints_PersistsAllDiscoveredEndpoints()
    {
        // Arrange
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

        // Act
        var result = await _sut.CreateModuleAsync(dto);

        // Assert
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
        // Arrange
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

        // Act
        var result = await _sut.CreateModuleAsync(dto);

        // Assert
        Assert.False(result.DiscrepancyReport!.HasDiscrepancy);
        Assert.Equal(ModuleStatus.Running, result.Module.Status);
        Assert.Single(result.DiscrepancyReport.MatchedEndpoints!);
        Assert.Null(result.DiscrepancyReport.MissingEndpoints);
        Assert.Null(result.DiscrepancyReport.ExtraEndpoints);
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ScannerFails_DoesNotPersistModuleOrLeaveStorageDirectory()
    {
        // Arrange
        _endpointScanner.ExceptionToThrow = new InvalidOperationException("Module scan failed.");
        var dto = new CreateModuleDto
        {
            ModuleName = "Invalid Module",
            BaseUrlPath = "api/v1/invalid",
            Version = "v1.0.0",
            ModuleFile = CreateDummyZipFile()
        };

        // Act & Assert
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
        // Arrange
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

        // Act
        var result = await _sut.CreateModuleAsync(dto);

        // Assert
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

    #endregion

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

    #region UpdateModule Tests

    [Fact]
    [Trait("Feature", "UpdateModule")]
    public async Task UpdateModuleAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var dto = new UpdateModuleDto { ModuleName = "Any Name" };

        // Act & Assert
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
        // Arrange
        var module = CreateTestModuleEntity("Original Name", "api/v1/orig", 8005);
        module.Description = "Original Description";
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        // Act
        await _sut.UpdateModuleAsync(module.Id, dto);

        // Assert
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
        // Arrange
        var module = CreateTestModuleEntity("Unchanged", "api/v1/unchanged", 8006);
        module.Description = "Same Description";
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var dto = new UpdateModuleDto
        {
            ModuleName = "Unchanged",
            Description = "Same Description"
        };

        // Act
        await _sut.UpdateModuleAsync(module.Id, dto);

        // Assert
        Assert.Equal(EntityState.Unchanged, _context.Entry(module).State);
    }

    #endregion

    #region UpdateModuleFiles Tests

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var dto = new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile() };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _moduleVersionService.CreateModuleVersionAsync(nonExistentId, dto));

        Assert.Equal($"Module with ID '{nonExistentId}' was not found.", exception.Message);
    }

    [Theory]
    [InlineData("extension", typeof(InvalidOperationException), "The module file must be a ZIP archive.")]
    [InlineData("corrupt", typeof(InvalidDataException), null)]
    [InlineData("entries", typeof(InvalidOperationException), "Module archive exceeds the maximum allowed number of entries.")]
    [InlineData("size", typeof(InvalidOperationException), "Module archive exceeds the maximum allowed uncompressed size.")]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_InvalidUpload_PreservesActiveVersion(
        string scenario, Type expectedExceptionType, string? expectedMessage)
    {
        var moduleDir = Path.Combine(_testRootDirectory, "protected-version");
        Directory.CreateDirectory(moduleDir);
        var existingFilePath = Path.Combine(moduleDir, "current_assembly.dll");
        await File.WriteAllTextAsync(existingFilePath, "current module contents");

        var module = CreateTestModuleEntity("Protected Module", "api/v1/protected", 8011);
        module.Versions.Single().StoragePath = moduleDir;
        module.ContainerId = "existing-container";
        module.Status = ModuleStatus.Running;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        IFormFile upload = scenario switch
        {
            "extension" => CreateDummyZipFile("archive.rar"),
            "corrupt" => new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "ModuleFile", "archive.zip"),
            "entries" => CreateZipFileWithEntries(1_001),
            "size" => CreateZipFileWithClaimedUncompressedSize(512U * 1024 * 1024 + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var exception = await Record.ExceptionAsync(() =>
            _moduleVersionService.CreateModuleVersionAsync(module.Id, new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = upload }));

        Assert.IsType(expectedExceptionType, exception);
        if (expectedMessage is not null)
            Assert.Equal(expectedMessage, exception!.Message);
        Assert.True(File.Exists(existingFilePath));
        Assert.Equal("current module contents", await File.ReadAllTextAsync(existingFilePath));
        Assert.Empty(_dockerService.Calls);
        Assert.Null(_endpointScanner.ScannedDirectory);
        Assert.Equal("existing-container", module.ContainerId);
        Assert.Equal(ModuleStatus.Running, module.Status);
        var persisted = await _context.Modules.AsNoTracking().Include(m => m.Versions).SingleAsync();
        Assert.Equal("existing-container", persisted.ContainerId);
        Assert.Equal(moduleDir, persisted.Versions.Single().StoragePath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_UnconfirmedEndpoints_RejectsUpdateBeforeDockerCalls(bool modulePending)
    {
        var module = CreateTestModuleEntity("Pending Update", "api/v1/pending-update", 8012);
        module.ContainerId = "existing-container";
        module.Status = modulePending ? ModuleStatus.PendingConfirmation : ModuleStatus.Running;
        if (!modulePending)
        {
            module.SubEndpoints.Add(new ModuleEndpoint
            {
                ModuleId = module.Id,
                ModuleVersion = module.Versions.Single(),
                HttpMethod = "GET",
                EndpointPath = "/health",
                Status = ModuleEndpointsStatus.PendingConfirmation
            });
        }
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _moduleVersionService.CreateModuleVersionAsync(module.Id, new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile() }));

        Assert.Equal("Module endpoints must be confirmed before updating module files.", exception.Message);
        Assert.Empty(_dockerService.Calls);
        Assert.Null(_endpointScanner.ScannedDirectory);
        Assert.Equal("existing-container", module.ContainerId);
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_ValidZip_ReplacesDirectoryContents()
    {
        // Arrange
        var moduleDir = Path.Combine(_testRootDirectory, "module_files_update");
        Directory.CreateDirectory(moduleDir);
        File.WriteAllText(Path.Combine(moduleDir, "old_file.txt"), "old");

        var module = CreateTestModuleEntity("File Update", "api/v1/updatefiles", 8008);
        module.Versions.Single().StoragePath = moduleDir;
        module.ContainerId = "existing-container";
        module.Status = ModuleStatus.Running;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _dockerService.BeforeReady = () =>
        {
            var latestVersion = module.Versions.Single(v => v.Version == "v1.1.0");
            Assert.Equal("replacement-container", module.ContainerId);
            Assert.Equal(_dockerService.BuiltStoragePath, latestVersion.StoragePath);
            Assert.Equal(ModuleStatus.Starting, module.Status);
            Assert.True(File.Exists(Path.Combine(moduleDir, "old_file.txt")));
            Assert.Empty(_dockerService.RemovedContainerIds);
            return Task.CompletedTask;
        };

        const string newFileName = "updated_assembly.dll";
        var newZip = CreateDummyZipFile("new_release.zip", newFileName);
        var dto = new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = newZip };

        // Act
        var result = await _moduleVersionService.CreateModuleVersionAsync(module.Id, dto);

        // Assert
        var newVersion = module.Versions.Single(v => v.Version == "v1.1.0");
        Assert.True(File.Exists(Path.Combine(moduleDir, "old_file.txt")));
        Assert.NotEqual(moduleDir, newVersion.StoragePath);
        Assert.True(File.Exists(Path.Combine(newVersion.StoragePath, newFileName)));
        Assert.Equal("replacement-container", module.ContainerId);
        Assert.Equal(ModuleStatus.Running, module.Status);
        Assert.Equal("module.dll", newVersion.ModuleEntryAssemblyFileName);
        Assert.Equal(["build", "start", "ready", "remove:existing-container"], _dockerService.Calls);
        Assert.Equal(module.Id, result.Module.Id);
        Assert.Equal(ModuleStatus.Running, result.Module.Status);
        Assert.Equal(newVersion.StoragePath, result.Module.Versions.Single(v => v.Version == "v1.1.0").StoragePath);
        var persisted = await _context.Modules.AsNoTracking().Include(m => m.Versions).SingleAsync(item => item.Id == module.Id);
        Assert.Equal("replacement-container", persisted.ContainerId);
        Assert.Equal(newVersion.StoragePath, persisted.Versions.Single(v => v.Version == "v1.1.0").StoragePath);
        Assert.Equal(ModuleStatus.Running, persisted.Status);
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("save")]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_Failure_PreservesActiveVersion(string failureStage)
    {
        var module = CreateTestModuleEntity("Active", "api/v1/active", 8020);
        var initialVersion = module.Versions.Single();
        initialVersion.StoragePath = Path.Combine(_testRootDirectory, "active-version");
        module.ContainerId = "existing-container";
        initialVersion.ModuleEntryAssemblyFileName = "original.dll";
        module.Status = ModuleStatus.Running;
        Directory.CreateDirectory(initialVersion.StoragePath);
        await File.WriteAllTextAsync(Path.Combine(initialVersion.StoragePath, "original.dll"), "original");
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        var originalPath = initialVersion.StoragePath;
        var failure = new InvalidOperationException("Simulated " + failureStage + " failure.");

        switch (failureStage)
        {
            case "scan": _endpointScanner.ExceptionToThrow = failure; break;
            case "save": _saveInterceptor.ExceptionToThrow = failure; break;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _moduleVersionService.CreateModuleVersionAsync(module.Id, new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile() }));

        Assert.Same(failure, exception);
        Assert.Equal("existing-container", module.ContainerId);
        Assert.Equal(originalPath, initialVersion.StoragePath);
        Assert.Equal("original.dll", initialVersion.ModuleEntryAssemblyFileName);
        Assert.Equal(ModuleStatus.Running, module.Status);
        Assert.True(File.Exists(Path.Combine(originalPath, "original.dll")));
        Assert.Equal(failureStage == "scan" ? Array.Empty<string>() : ["replacement-container"], _dockerService.RemovedContainerIds);
        Assert.Equal(failureStage == "scan" ? Array.Empty<string>() : ["build", "start", "ready", "remove:replacement-container"], _dockerService.Calls);
        Assert.False(Directory.Exists(_endpointScanner.ScannedDirectory));
        var persisted = await _context.Modules.AsNoTracking().Include(m => m.Versions).SingleAsync(item => item.Id == module.Id);
        Assert.Equal("existing-container", persisted.ContainerId);
        Assert.Equal(originalPath, persisted.Versions.Single().StoragePath);
        Assert.Equal("original.dll", persisted.Versions.Single().ModuleEntryAssemblyFileName);
        Assert.Equal(ModuleStatus.Running, persisted.Status);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("start")]
    [InlineData("readiness")]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_DockerFailure_PersistsFailedStatus(string failureStage)
    {
        var module = CreateTestModuleEntity("Docker failure", "api/v1/docker-failure", 8025);
        module.ContainerId = "existing-container";
        module.Status = ModuleStatus.Running;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        var failure = new InvalidOperationException("Simulated " + failureStage + " failure.");

        switch (failureStage)
        {
            case "build": _dockerService.BuildException = failure; break;
            case "start": _dockerService.StartException = failure; break;
            case "readiness": _dockerService.ReadinessException = failure; break;
        }

        var result = await _moduleVersionService.CreateModuleVersionAsync(module.Id,
            new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile() });

        var expectedCalls = failureStage switch
        {
            "build" => new[] { "build" },
            "start" => ["build", "start"],
            _ => ["build", "start", "ready"]
        };
        var newVersionDto = result.Module.Versions.Single(v => v.Version == "v1.1.0");
        Assert.Equal(expectedCalls, _dockerService.Calls);
        Assert.Empty(_dockerService.RemovedContainerIds);
        Assert.Equal(ModuleStatus.Failed, result.Module.Status);
        Assert.Equal(_endpointScanner.ScannedDirectory, newVersionDto.StoragePath);
        Assert.True(File.Exists(Path.Combine(newVersionDto.StoragePath!, "service.dll")));
        var persisted = await _context.Modules.AsNoTracking().Include(m => m.Versions).SingleAsync(item => item.Id == module.Id);
        var persistedNewVersion = persisted.Versions.Single(v => v.Version == "v1.1.0");
        Assert.Equal(ModuleStatus.Failed, persisted.Status);
        Assert.Equal(newVersionDto.StoragePath, persistedNewVersion.StoragePath);
        Assert.Equal("module.dll", persistedNewVersion.ModuleEntryAssemblyFileName);
        Assert.Equal(failureStage == "build" ? "existing-container" : "replacement-container", persisted.ContainerId);
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_ScannerBusy_QueuesUpdateAndPreservesPreviousFiles()
    {
        var module = CreateTestModuleEntity("Active module", "api/active", 8080);
        module.ContainerId = "active-container";
        module.Status = ModuleStatus.Running;
        var initialVersion = module.Versions.Single();
        initialVersion.StoragePath = Path.Combine(_testRootDirectory, "active-version");
        Directory.CreateDirectory(initialVersion.StoragePath);
        await File.WriteAllTextAsync(Path.Combine(initialVersion.StoragePath, "module.dll"), "active files");
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _endpointScanner.WasQueued = true;
        var originalPath = initialVersion.StoragePath;

        var result = await _moduleVersionService.CreateModuleVersionAsync(module.Id,
            new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile() });

        var newVersionDto = result.Module.Versions.Single(v => v.Version == "v1.1.0");
        Assert.Equal(ModuleStatus.QueuedForScan, result.Module.Status);
        Assert.Equal(module.Id, result.Module.Id);
        Assert.NotEqual(originalPath, newVersionDto.StoragePath);
        Assert.Equal(_endpointScanner.ScannedDirectory, newVersionDto.StoragePath);
        Assert.Contains("Warteschlange", result.Message);
        Assert.Empty(_endpointScanner.CancelledModuleIds);
        Assert.Empty(_dockerService.Calls);
        Assert.True(File.Exists(Path.Combine(newVersionDto.StoragePath!, "service.dll")));
        Assert.Equal("active files", await File.ReadAllTextAsync(Path.Combine(originalPath, "module.dll")));
        var persisted = await _context.Modules.AsNoTracking().Include(m => m.Versions).SingleAsync();
        Assert.Equal("active-container", persisted.ContainerId);
        Assert.Equal(newVersionDto.StoragePath, persisted.Versions.Single(v => v.Version == "v1.1.0").StoragePath);
        Assert.Equal(ModuleStatus.QueuedForScan, persisted.Status);
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_EndpointContractChanges_RequiresConfirmation()
    {
        var module = CreateTestModuleEntity("Contract", "api/v1/contract", 8021);
        module.ContainerId = "existing-container";
        module.Status = ModuleStatus.Running;
        module.SubEndpoints.Add(new ModuleEndpoint
        {
            ModuleId = module.Id, ModuleVersion = module.Versions.Single(), HttpMethod = "GET", EndpointPath = "/original", Status = ModuleEndpointsStatus.Active
        });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _endpointScanner.DiscoveredEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/changed" }];

        var result = await _moduleVersionService.CreateModuleVersionAsync(module.Id,
            new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile() });

        var newVersionDto = result.Module.Versions.Single(v => v.Version == "v1.1.0");
        Assert.Empty(_dockerService.Calls);
        Assert.Equal("existing-container", module.ContainerId);
        Assert.Equal(ModuleStatus.PendingConfirmation, result.Module.Status);
        Assert.Equal(_endpointScanner.ScannedDirectory, newVersionDto.StoragePath);
        Assert.True(Directory.Exists(newVersionDto.StoragePath));
        var persisted = await _context.Modules.AsNoTracking().Include(item => item.SubEndpoints).Include(item => item.Versions).SingleAsync(item => item.Id == module.Id);
        var persistedNewVersion = persisted.Versions.Single(v => v.Version == "v1.1.0");
        Assert.Equal(ModuleStatus.PendingConfirmation, persisted.Status);
        Assert.Equal("existing-container", persisted.ContainerId);
        Assert.Equal("module.dll", persistedNewVersion.ModuleEntryAssemblyFileName);
        Assert.Equal(newVersionDto.StoragePath, persistedNewVersion.StoragePath);
        Assert.Equal(new[] { "/changed", "/original" }, persisted.SubEndpoints.Select(endpoint => endpoint.EndpointPath).Order());
        Assert.All(persisted.SubEndpoints, endpoint => Assert.Equal(ModuleEndpointsStatus.PendingConfirmation, endpoint.Status));
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_ConcurrentActivation_DoesNotOverwriteWinningVersion()
    {
        var module = CreateTestModuleEntity("Concurrent", "api/v1/concurrent", 8022);
        module.ContainerId = "existing-container";
        module.Status = ModuleStatus.Running;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _dockerService.BeforeReady = async () =>
        {
            await _context.Modules.Where(item => item.Id == module.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ContainerId, "winning-container")
                .SetProperty(item => item.CurrentVersion, "v2.0.0"));
        };

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            _moduleVersionService.CreateModuleVersionAsync(module.Id, new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile() }));

        var persisted = await _context.Modules.AsNoTracking().SingleAsync(item => item.Id == module.Id);
        Assert.Equal("winning-container", persisted.ContainerId);
        Assert.Equal("v2.0.0", persisted.CurrentVersion);
        Assert.Equal(["build", "start", "ready", "remove:replacement-container"], _dockerService.Calls);
        Assert.Equal(["replacement-container"], _dockerService.RemovedContainerIds);
        Assert.False(Directory.Exists(_dockerService.BuiltStoragePath));
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_Cancellation_RestoresModuleAndDeletesExtractedFiles()
    {
        var module = CreateTestModuleEntity("Canceled", "api/v1/canceled", 8023);
        module.ContainerId = "existing-container";
        module.Status = ModuleStatus.Running;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        var originalPath = module.Versions.Single().StoragePath;
        using var cancellation = new CancellationTokenSource();
        _dockerService.BeforeReady = () =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _moduleVersionService.CreateModuleVersionAsync(module.Id, new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile() }, cancellation.Token));

        Assert.Equal("existing-container", module.ContainerId);
        Assert.Equal(originalPath, module.Versions.Single().StoragePath);
        Assert.Equal(ModuleStatus.Running, module.Status);
        Assert.Equal(["replacement-container"], _dockerService.RemovedContainerIds);
        Assert.False(Directory.Exists(_dockerService.BuiltStoragePath));
        var persisted = await _context.Modules.AsNoTracking().Include(m => m.Versions).SingleAsync(item => item.Id == module.Id);
        Assert.Equal("existing-container", persisted.ContainerId);
        Assert.Equal(originalPath, persisted.Versions.Single().StoragePath);
        Assert.Equal(ModuleStatus.Running, persisted.Status);
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_QueuedScanResult_StartsUpdatedModule()
    {
        var module = CreateTestModuleEntity("Queued update", "api/v1/queued-update", 8024);
        module.ContainerId = "existing-container";
        module.Status = ModuleStatus.Running;
        var initialVersion = module.Versions.Single();
        initialVersion.ModuleEntryAssemblyFileName = "original.dll";
        initialVersion.StoragePath = Path.Combine(_testRootDirectory, "retained-version");
        Directory.CreateDirectory(initialVersion.StoragePath);
        var previousPath = initialVersion.StoragePath;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _endpointScanner.WasQueued = true;

        var result = await _moduleVersionService.CreateModuleVersionAsync(module.Id,
            new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile(containedEntryName: "updated.dll") });

        var newVersionDto = result.Module.Versions.Single(v => v.Version == "v1.1.0");
        Assert.Equal(ModuleStatus.QueuedForScan, result.Module.Status);
        Assert.Equal("original.dll", initialVersion.ModuleEntryAssemblyFileName);
        Assert.Empty(_dockerService.Calls);

        await _scannerService.ProcessQueuedScanResultAsync(module.Id, new ModuleScanResultDto
        {
            EntryAssemblyFileName = "updated.dll",
            DiscoveredEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/health" }]
        });

        Assert.Equal("replacement-container", module.ContainerId);
        Assert.Equal(ModuleStatus.Running, module.Status);
        Assert.Equal(["build", "start", "ready"], _dockerService.Calls);
        Assert.Equal(newVersionDto.StoragePath, _dockerService.BuiltStoragePath);
        Assert.Equal("updated.dll", _dockerService.BuiltEntryAssembly);
        Assert.True(File.Exists(Path.Combine(newVersionDto.StoragePath!, "updated.dll")));
        Assert.True(Directory.Exists(previousPath));
        var persisted = await _context.Modules.AsNoTracking().Include(item => item.SubEndpoints).Include(item => item.Versions).SingleAsync(item => item.Id == module.Id);
        var persistedNewVersion = persisted.Versions.Single(v => v.Version == "v1.1.0");
        Assert.Equal("replacement-container", persisted.ContainerId);
        Assert.Equal(newVersionDto.StoragePath, persistedNewVersion.StoragePath);
        Assert.Equal(ModuleStatus.Running, persisted.Status);
        Assert.Equal("updated.dll", persistedNewVersion.ModuleEntryAssemblyFileName);
        var endpoint = Assert.Single(persisted.SubEndpoints);
        Assert.Equal("GET", endpoint.HttpMethod);
        Assert.Equal("/health", endpoint.EndpointPath);
        Assert.Equal(ModuleEndpointsStatus.Active, endpoint.Status);
    }

    #endregion

    #region DeleteModule Tests

    [Fact]
    [Trait("Feature", "DeleteModule")]
    public async Task DeleteModuleAsync_ModuleNotFound_CompletesSilently()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act & Assert (Should not throw)
        await _sut.DeleteModuleAsync(nonExistentId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Feature", "DeleteModule")]
    public async Task DeleteModuleAsync_ModuleExists_DeletesStorageAndEndpoints(bool hasContainer)
    {
        // Arrange
        var moduleDir = Path.Combine(_testRootDirectory, "module_to_delete");
        Directory.CreateDirectory(moduleDir);
        File.WriteAllText(Path.Combine(moduleDir, "test.txt"), "delete me");

        var module = CreateTestModuleEntity("To Delete", "api/v1/delete", 8009);
        module.Versions.Single().StoragePath = moduleDir;
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

        // Act
        await _sut.DeleteModuleAsync(module.Id);

        // Assert - Directory deleted
        Assert.False(Directory.Exists(moduleDir));

        // Assert - Database records removed
        Assert.Null( await _context.Modules.FindAsync(module.Id));
        Assert.Null(await _context.ModuleEndpoints.FindAsync(endpoint.Id));
        Assert.Equal(hasContainer ? ["existing-container"] : Array.Empty<string>(), _dockerService.RemovedContainerIds);
    }

    [Fact]
    [Trait("Feature", "DeleteModule")]
    public async Task DeleteModuleAsync_QueuedForScan_CancelsScanAndDeletesModule()
    {
        // Arrange
        var moduleDir = Path.Combine(_testRootDirectory, "queued_module_to_delete");
        Directory.CreateDirectory(moduleDir);

        var module = CreateTestModuleEntity("Queued Delete", "api/v1/queued-delete", 8010);
        module.Versions.Single().StoragePath = moduleDir;
        module.Status = ModuleStatus.QueuedForScan;

        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        // Act
        await _sut.DeleteModuleAsync(module.Id);

        // Assert
        Assert.Equal([module.Id], _endpointScanner.CancelledModuleIds);
        Assert.False(Directory.Exists(moduleDir));
        Assert.Null(await _context.Modules.FindAsync(module.Id));
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
            Status = ModuleEndpointsStatus.Active
        });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var result = await _sut.StartModuleAsync(module.Id);

        Assert.Equal(ModuleStatus.Running, result.Status);
        Assert.Equal("existing-container", result.ContainerId);
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
        _reverseProxyService.UpsertModule(new Modulix.ReverseProxy.Models.ProxyModuleDefinition
        {
            ModuleId = module.Id,
            BaseUrlPath = module.BaseUrlPath,
            ContainerPort = module.ContainerPort,
            Endpoints = [new Modulix.ReverseProxy.Models.ProxyEndpointDefinition { HttpMethod = "GET", EndpointPath = "/ping" }]
        });

        var result = await _sut.StopModuleAsync(module.Id);

        Assert.Equal(ModuleStatus.Stopped, result.Status);
        Assert.Equal(["stop:running-container"], _dockerService.Calls);
        Assert.DoesNotContain(_reverseProxyService.GetConfig().Routes, route => route.ClusterId == $"module-cluster-{module.Id:N}");
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

    #region ReverseProxy Synchronization Tests

    [Fact]
    [Trait("Feature", "ReverseProxySynchronization")]
    public async Task ModuleLifecycle_SynchronizesRoutesWithReverseProxy()
    {
        // 1. Create a module with discrepancies -> PendingConfirmation (not routed yet)
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

        // 2. Confirm extra endpoint -> Running (cluster and active routes registered)
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

        // 3. Update module files while scanner is busy -> QueuedForScan (removed from proxy)
        _endpointScanner.WasQueued = true;
        var queuedUpdateResult = await _moduleVersionService.CreateModuleVersionAsync(moduleId, new ModuleVersionCreateDto
        {
            Version = "v1.1.0",
            ModuleFile = CreateDummyZipFile()
        });

        Assert.Equal(ModuleStatus.QueuedForScan, queuedUpdateResult.Module.Status);
        Assert.Empty(_reverseProxyService.GetConfig().Clusters);
        Assert.Empty(_reverseProxyService.GetConfig().Routes);

        // 4. Process queued scan result with matching endpoints -> Running (re-registered in proxy)
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

        // 5. Delete module -> Removed from proxy
        await _sut.DeleteModuleAsync(moduleId);
        Assert.Empty(_reverseProxyService.GetConfig().Clusters);
        Assert.Empty(_reverseProxyService.GetConfig().Routes);
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

    private static IFormFile CreateDummyZipFile(string fileName = "module.zip", string containedEntryName = "service.dll")
    {
        var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(containedEntryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("Mock assembly bytes");
        }

        memoryStream.Position = 0;
        return new FormFile(memoryStream, 0, memoryStream.Length, "ModuleFile", fileName);
    }

    private static IFormFile CreateZipFileWithEntries(int entryCount)
    {
        var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
                archive.CreateEntry($"file-{entryIndex}.txt");
        }

        memoryStream.Position = 0;
        return new FormFile(memoryStream, 0, memoryStream.Length, "ModuleFile", "many-files.zip");
    }

    private static IFormFile CreateZipFileWithClaimedUncompressedSize(uint uncompressedSize)
    {
        var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("payload.bin");
        }

        var archiveBytes = memoryStream.ToArray();
        var centralDirectoryOffset = FindCentralDirectoryOffset(archiveBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(archiveBytes.AsSpan(centralDirectoryOffset + 24, sizeof(uint)), uncompressedSize);

        var archiveStream = new MemoryStream(archiveBytes);
        return new FormFile(archiveStream, 0, archiveStream.Length, "ModuleFile", "oversized.zip");
    }

    private static int FindCentralDirectoryOffset(byte[] archiveBytes)
    {
        for (var byteIndex = 0; byteIndex <= archiveBytes.Length - 4; byteIndex++)
        {
            if (archiveBytes[byteIndex] == 0x50 &&
                archiveBytes[byteIndex + 1] == 0x4B &&
                archiveBytes[byteIndex + 2] == 0x01 &&
                archiveBytes[byteIndex + 3] == 0x02)
            {
                return byteIndex;
            }
        }

        throw new InvalidOperationException("ZIP archive does not contain a central directory entry.");
    }

    private void AssertNoStoredModuleDirectories()
    {
        var modulesStorageDirectory = Path.Combine(_testRootDirectory, "storage", "modules");

        Assert.True(Directory.Exists(modulesStorageDirectory));
        Assert.Empty(Directory.EnumerateDirectories(modulesStorageDirectory));
    }

    private sealed class FakeModuleEndpointScanner : IModuleEndpointScanner
    {
        public List<DiscoveredSubEndpointDto> DiscoveredEndpoints { get; set; } = [];

        public Exception? ExceptionToThrow { get; set; }

        public string? ScannedDirectory { get; private set; }

        public bool WasQueued { get; set; }
        public List<Guid> CancelledModuleIds { get; } = [];
        public List<ModuleScanQueueItemDto> QueuedScans { get; set; } = [];

        public bool CancelScan(Guid moduleId)
        {
            CancelledModuleIds.Add(moduleId);
            return WasQueued;
        }

        public IReadOnlyList<ModuleScanQueueItemDto> GetQueuedScans() => QueuedScans;

        public Task<ScanEnqueueResponse> ScanOrEnqueueAsync(
            Guid moduleId, string moduleDirectoryPath, ScanPriority priority = ScanPriority.Create, CancellationToken ct = default)
        {
            ScannedDirectory = moduleDirectoryPath;
            ct.ThrowIfCancellationRequested();

            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return Task.FromResult(new ScanEnqueueResponse
            {
                WasQueued = WasQueued,
                Result = WasQueued ? null : new ModuleScanResultDto
                {
                    EntryAssemblyFileName = "module.dll",
                    DiscoveredEndpoints = DiscoveredEndpoints
                }
            });
        }
    }

    private sealed class FakeDockerService : IDockerService
    {
        public List<string> RemovedContainerIds { get; } = [];
        public List<string> Calls { get; } = [];
        public string? BuiltStoragePath { get; private set; }
        public string? BuiltEntryAssembly { get; private set; }
        public bool IsContainerRunning { get; set; } = true;
        public string ContainerLogsResult { get; set; } = string.Empty;
        public Exception? BuildException { get; set; }
        public Exception? StartException { get; set; }
        public Exception? ReadinessException { get; set; }
        public Exception? RemoveException { get; set; }
        public Exception? LogsException { get; set; }
        public Func<Task>? BeforeReady { get; set; }
        public Func<string, Task>? BeforeRemove { get; set; }

        public Task<string> BuildContainerAsync(Guid moduleId, string storagePath, string entryDllName, int containerPort, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add("build");
            BuiltStoragePath = storagePath;
            BuiltEntryAssembly = entryDllName;
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

        public async Task WaitUntilReadyAsync(string containerId, CancellationToken ct = default)
        {
            Calls.Add("ready");
            if (BeforeReady is not null)
                await BeforeReady();
            ct.ThrowIfCancellationRequested();
            if (ReadinessException is not null)
                throw ReadinessException;
        }

        public Task StopContainerAsync(string containerId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add("stop:" + containerId);
            return Task.CompletedTask;
        }

        public async Task RemoveContainerAsync(string containerId, Guid moduleId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add("remove:" + containerId);
            RemovedContainerIds.Add(containerId);
            if (BeforeRemove is not null)
                await BeforeRemove(containerId);
            if (RemoveException is not null)
                throw RemoveException;
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

        public Task<bool> IsContainerRunningAsync(string containerId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(IsContainerRunning);
        }

        public Task<string> GetContainerLogsAsync(string containerId, int? tail = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add($"logs:{containerId}:{tail?.ToString() ?? "all"}");
            if (LogsException is not null)
                throw LogsException;
            return Task.FromResult(ContainerLogsResult);
        }
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