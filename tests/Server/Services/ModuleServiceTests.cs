using System.Buffers.Binary;
using System.Text;
using System.IO.Compression;

using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

using Server.Database.Entities;
using Server.Database.DbContexts;

using Server.Models.Dtos;
using Server.Models.Enums;

using Server.Services.Interfaces;

using Server.TestData;

namespace Server.Services;

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
    private readonly FailingSaveInterceptor _saveInterceptor = new();
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

        var fakeEnv = new FakeHostEnvironment(_testRootDirectory);
        _endpointScanner = new FakeModuleEndpointScanner();
        _dockerService = new FakeDockerService();

        _sut = new ModuleService(
            _context,
            NullLogger<ModuleService>.Instance,
            fakeEnv,
            _dockerService,
            _endpointScanner);
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
    [ClassData(typeof(InvalidBaseEndpointPathsTestData))]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_InvalidBaseEndpointPath_ThrowsArgumentException(string? invalidPath)
    {
        // Arrange
        var dto = new CreateModuleDto
        {
            ModuleName = "Test Module",
            BaseEndpointPath = invalidPath!,
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
            BaseEndpointPath = "api/v1/new",
            ContainerPort = 8080,
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
            BaseEndpointPath = "api/v1/autoport",
            ContainerPort = null,
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
    [ClassData(typeof(ConflictingBaseEndpointPathsTestData))]
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
            BaseEndpointPath = newPath,
            ContainerPort = 8081,
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
            BaseEndpointPath = "api/v1/empty",
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
            BaseEndpointPath = "api/v1/invalidext",
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
            BaseEndpointPath = "/api/v1/analytics/",
            ContainerPort = 9000,
            ModuleFile = zipFile
        };

        // Act
        var result = await _sut.CreateModuleAsync(dto);

        // Assert - Return DTO
        Assert.NotNull(result);
        Assert.Equal("Valid Analytics", result.Module.ModuleName);
        Assert.Equal("api/v1/analytics", result.Module.BaseEndpointPath);
        Assert.Equal(9000, result.Module.ContainerPort);
        Assert.True(Directory.Exists(result.Module.StoragePath));
        Assert.True(File.Exists(Path.Combine(result.Module.StoragePath, entryName)));
        Assert.Equal(result.Module.StoragePath, _endpointScanner.ScannedDirectory);
        Assert.False(result.DiscrepancyReport!.HasDiscrepancy);
        Assert.Equal(ModuleStatus.Running, result.Module.Status);

        // Assert - Database state
        var persisted = await _context.Modules.FindAsync(result.Module.Id);
        Assert.NotNull(persisted);
        Assert.Equal("api/v1/analytics", persisted.BaseEndpointPath);
        Assert.Equal(result.Module.StoragePath, persisted.StoragePath);
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_NoInitialEndpoints_PersistsAllDiscoveredEndpoints()
    {
        // Arrange
        _endpointScanner.DiscoveredEndpoints =
        [
            new DiscoveredEndpointDto { HttpMethod = "GET", EndpointPath = "/health" },
            new DiscoveredEndpointDto { HttpMethod = "POST", EndpointPath = "/jobs" }
        ];

        var dto = new CreateModuleDto
        {
            ModuleName = "Scanned Module",
            BaseEndpointPath = "api/v1/scanned",
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

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_AllInitialEndpointsMatch_ReturnsMatchedReportAndCreatedModule()
    {
        // Arrange
        _endpointScanner.DiscoveredEndpoints =
        [
            new DiscoveredEndpointDto { HttpMethod = "GET", EndpointPath = "/health" }
        ];

        var dto = new CreateModuleDto
        {
            ModuleName = "Matched Module",
            BaseEndpointPath = "api/v1/matched",
            ModuleFile = CreateDummyZipFile(),
            InitialEndpoints =
            [
                new CreateModuleEndpointDto { HttpMethod = "GET", EndpointPath = "/health" }
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
            BaseEndpointPath = "api/v1/invalid",
            ModuleFile = CreateDummyZipFile()
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.CreateModuleAsync(dto));

        Assert.Equal("Module scan failed.", exception.Message);
        Assert.Empty(await _context.Modules.ToListAsync());
        AssertNoStoredModuleDirectories();
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ArchiveExceedsEntryLimit_DoesNotPersistModuleOrLeaveStorageDirectory()
    {
        // Arrange
        var dto = new CreateModuleDto
        {
            ModuleName = "Oversized Module",
            BaseEndpointPath = "api/v1/oversized",
            ModuleFile = CreateZipFileWithEntries(1_001)
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.CreateModuleAsync(dto));

        Assert.Equal("Module archive exceeds the maximum allowed number of entries.", exception.Message);
        Assert.Empty(await _context.Modules.ToListAsync());
        AssertNoStoredModuleDirectories();
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ArchiveExceedsUncompressedSizeLimit_DoesNotPersistModuleOrLeaveStorageDirectory()
    {
        // Arrange
        var dto = new CreateModuleDto
        {
            ModuleName = "Oversized Archive Module",
            BaseEndpointPath = "api/v1/oversized-archive",
            ModuleFile = CreateZipFileWithClaimedUncompressedSize(512U * 1024 * 1024 + 1)
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.CreateModuleAsync(dto));

        Assert.Equal("Module archive exceeds the maximum allowed uncompressed size.", exception.Message);
        Assert.Empty(await _context.Modules.ToListAsync());
        AssertNoStoredModuleDirectories();
    }

    [Fact]
    [Trait("Feature", "CreateModule")]
    public async Task CreateModuleAsync_ReconcilesInitialAndDiscoveredEndpoints()
    {
        // Arrange
        _endpointScanner.DiscoveredEndpoints =
        [
            new DiscoveredEndpointDto { HttpMethod = "GET", EndpointPath = "/health" },
            new DiscoveredEndpointDto { HttpMethod = "DELETE", EndpointPath = "/legacy" }
        ];

        var dto = new CreateModuleDto
        {
            ModuleName = "Reconciled Module",
            BaseEndpointPath = "api/v1/reconciled",
            ModuleFile = CreateDummyZipFile(),
            InitialEndpoints =
            [
                new CreateModuleEndpointDto { HttpMethod = "GET", EndpointPath = "/health" },
                new CreateModuleEndpointDto { HttpMethod = "POST", EndpointPath = "/jobs" }
            ]
        };

        // Act
        var result = await _sut.CreateModuleAsync(dto);

        // Assert
        Assert.True(result.DiscrepancyReport!.HasDiscrepancy);
        Assert.Equal(ModuleStatus.PendingConfirmation, result.Module.Status);
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

    #region ConfirmEndpoints Tests

    [Fact]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmEndpointsAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var dto = new ConfirmEndpointsDto();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.ConfirmEndpointsAsync(nonExistentId, dto));

        Assert.Equal($"Module with ID '{nonExistentId}' was not found.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmEndpointsAsync_ModuleNotInPendingConfirmationStatus_ThrowsInvalidOperationException()
    {
        // Arrange
        var module = CreateTestModuleEntity("Active Module", "api/v1/active", 8080);
        module.Status = ModuleStatus.Created;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var dto = new ConfirmEndpointsDto();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ConfirmEndpointsAsync(module.Id, dto));

        Assert.Equal($"Module '{module.Id}' is not in PendingConfirmation status.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmEndpointsAsync_ValidDiscrepancies_ActivatesMissingAndRemovesExtraEndpoints()
    {
        // Arrange
        var module = CreateTestModuleEntity("Pending Module", "api/v1/pending", 8080);
        module.Status = ModuleStatus.PendingConfirmation;

        var missingEndpoint = new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            HttpMethod = "GET",
            EndpointPath = "/health",
            Status = ModuleEndpointsStatus.PendingConfirmation
        };

        var extraEndpoint = new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
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
                new EndpointDiscrepancyReportDto
                {
                    ModuleId = module.Id,
                    MissingEndpoints = [new DiscoveredEndpointDto { HttpMethod = missingEndpoint.HttpMethod, EndpointPath = missingEndpoint.EndpointPath }],
                    ExtraEndpoints = [new DiscoveredEndpointDto { HttpMethod = extraEndpoint.HttpMethod, EndpointPath = extraEndpoint.EndpointPath }]
                }
            ]
        };

        // Act
        var result = await _sut.ConfirmEndpointsAsync(module.Id, confirmDto);

        // Assert - Return DTO
        Assert.Equal(ModuleStatus.Running, result.Status);

        // Assert - Database state
        var updatedMissing = await _context.ModuleEndpoints.FindAsync(missingEndpoint.Id);
        Assert.NotNull(updatedMissing);
        Assert.Equal(ModuleEndpointsStatus.Active, updatedMissing.Status);

        var removedExtra = await _context.ModuleEndpoints.FindAsync(extraEndpoint.Id);
        Assert.Null(removedExtra);
    }

    [Fact]
    [Trait("Feature", "ConfirmEndpoints")]
    public async Task ConfirmEndpointsAsync_SamePathWithDifferentHttpMethod_DoesNotConfirmEndpoint()
    {
        // Arrange
        var module = CreateTestModuleEntity("Pending Module", "api/v1/pending-method", 8082);
        module.Status = ModuleStatus.PendingConfirmation;

        var pendingEndpoint = new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
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
                new EndpointDiscrepancyReportDto
                {
                    ModuleId = module.Id,
                    MissingEndpoints = [new DiscoveredEndpointDto { HttpMethod = "POST", EndpointPath = "/health" }]
                }
            ]
        };

        // Act
        await _sut.ConfirmEndpointsAsync(module.Id, confirmDto);

        // Assert
        var updatedEndpoint = await _context.ModuleEndpoints.FindAsync(pendingEndpoint.Id);
        Assert.NotNull(updatedEndpoint);
        Assert.Equal(ModuleEndpointsStatus.PendingConfirmation, updatedEndpoint.Status);
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
        Assert.Empty(result);
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
        var result = (await _sut.GetAllModulesAsync()).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, m => m.Id == module1.Id && m.ModuleName == "Module 1");
        Assert.Contains(result, m => m.Id == module2.Id && m.ModuleName == "Module 2");
    }

    #endregion

    #region GetModuleById Tests

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
        var ep1 = new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = module.Id, HttpMethod = "GET", EndpointPath = "/a" };
        var ep2 = new ModuleEndpoint { Id = Guid.NewGuid(), ModuleId = module.Id, HttpMethod = "POST", EndpointPath = "/b" };

        _context.Modules.Add(module);
        _context.ModuleEndpoints.AddRange(ep1, ep2);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _sut.GetModuleSubEndpointsAsync(module.Id)).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.EndpointPath == "/a" && e.HttpMethod == "GET");
        Assert.Contains(result, e => e.EndpointPath == "/b" && e.HttpMethod == "POST");
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
        var dto = new UpdateModuleFilesDto { ModuleFile = CreateDummyZipFile() };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdateModuleFilesAsync(nonExistentId, dto));

        Assert.Equal($"Module with ID '{nonExistentId}' was not found.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_NonZipFile_ThrowsInvalidOperationException()
    {
        // Arrange
        var module = CreateTestModuleEntity("Storage Mod", "api/v1/files", 8007);
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var stream = new MemoryStream(Encoding.UTF8.GetBytes("raw content"));
        var invalidFile = new FormFile(stream, 0, stream.Length, "ModuleFile", "archive.rar");

        var dto = new UpdateModuleFilesDto { ModuleFile = invalidFile };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.UpdateModuleFilesAsync(module.Id, dto));

        Assert.Equal("The module file must be a ZIP archive.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_NonZipFile_PreservesExistingDirectoryContents()
    {
        // Arrange
        var moduleDir = Path.Combine(_testRootDirectory, "module_invalid_update");
        Directory.CreateDirectory(moduleDir);
        var existingFilePath = Path.Combine(moduleDir, "current_assembly.dll");
        File.WriteAllText(existingFilePath, "current module contents");

        var module = CreateTestModuleEntity("Protected Storage", "api/v1/protected", 8010);
        module.StoragePath = moduleDir;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a zip archive"));
        var invalidFile = new FormFile(stream, 0, stream.Length, "ModuleFile", "archive.tar");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.UpdateModuleFilesAsync(module.Id, new UpdateModuleFilesDto { ModuleFile = invalidFile }));

        Assert.True(File.Exists(existingFilePath));
        Assert.Equal("current module contents", await File.ReadAllTextAsync(existingFilePath));
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_CorruptedZip_PreservesExistingDirectoryContents()
    {
        // Arrange
        var moduleDir = Path.Combine(_testRootDirectory, "module_corrupted_update");
        Directory.CreateDirectory(moduleDir);
        var existingFilePath = Path.Combine(moduleDir, "current_assembly.dll");
        File.WriteAllText(existingFilePath, "current module contents");

        var module = CreateTestModuleEntity("Corrupted Archive", "api/v1/corrupted", 8011);
        module.StoragePath = moduleDir;
        module.ContainerId = "existing-container";
        module.Status = ModuleStatus.Running;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a valid zip archive"));
        var corruptedZip = new FormFile(stream, 0, stream.Length, "ModuleFile", "archive.zip");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _sut.UpdateModuleFilesAsync(module.Id, new UpdateModuleFilesDto { ModuleFile = corruptedZip }));

        Assert.True(File.Exists(existingFilePath));
        Assert.Equal("current module contents", await File.ReadAllTextAsync(existingFilePath));
        Assert.Empty(_dockerService.RemovedContainerIds);
        Assert.Equal("existing-container", module.ContainerId);
        Assert.Equal(ModuleStatus.Running, module.Status);
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_ArchiveExceedsEntryLimit_PreservesExistingDirectoryContents()
    {
        // Arrange
        var moduleDir = Path.Combine(_testRootDirectory, "module_entry_limit");
        Directory.CreateDirectory(moduleDir);
        var existingFilePath = Path.Combine(moduleDir, "current_assembly.dll");
        File.WriteAllText(existingFilePath, "current module contents");

        var module = CreateTestModuleEntity("Entry Limit", "api/v1/entry-limit", 8012);
        module.StoragePath = moduleDir;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var oversizedArchive = CreateZipFileWithEntries(1_001);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.UpdateModuleFilesAsync(module.Id, new UpdateModuleFilesDto { ModuleFile = oversizedArchive }));

        Assert.Equal("Module archive exceeds the maximum allowed number of entries.", exception.Message);
        Assert.True(File.Exists(existingFilePath));
        Assert.Equal("current module contents", await File.ReadAllTextAsync(existingFilePath));
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
        module.StoragePath = moduleDir;
        module.ContainerId = "existing-container";
        module.Status = ModuleStatus.Running;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _dockerService.BeforeReady = () =>
        {
            Assert.Equal("existing-container", module.ContainerId);
            Assert.True(File.Exists(Path.Combine(moduleDir, "old_file.txt")));
            Assert.Empty(_dockerService.RemovedContainerIds);
            return Task.CompletedTask;
        };
        _dockerService.BeforeRemove = async containerId =>
        {
            if (containerId == "existing-container")
            {
                var persisted = await _context.Modules.AsNoTracking().SingleAsync(item => item.Id == module.Id);
                Assert.Equal("replacement-container", persisted.ContainerId);
                Assert.Equal(_dockerService.BuiltStoragePath, persisted.StoragePath);
            }
        };

        const string newFileName = "updated_assembly.dll";
        var newZip = CreateDummyZipFile("new_release.zip", newFileName);
        var dto = new UpdateModuleFilesDto { ModuleFile = newZip };

        // Act
        await _sut.UpdateModuleFilesAsync(module.Id, dto);

        // Assert
        Assert.False(File.Exists(Path.Combine(moduleDir, "old_file.txt")));
        Assert.NotEqual(moduleDir, module.StoragePath);
        Assert.True(File.Exists(Path.Combine(module.StoragePath, newFileName)));
        Assert.Equal("replacement-container", module.ContainerId);
        Assert.Equal(ModuleStatus.Running, module.Status);
        Assert.Equal("module.dll", module.ModuleEntryAssemblyFileName);
        Assert.Equal(["build", "start", "ready", "remove:existing-container"], _dockerService.Calls);
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("build")]
    [InlineData("start")]
    [InlineData("readiness")]
    [InlineData("save")]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_Failure_PreservesActiveVersion(string failureStage)
    {
        var module = CreateTestModuleEntity("Active", "api/v1/active", 8020);
        module.StoragePath = Path.Combine(_testRootDirectory, "active-version");
        module.ContainerId = "existing-container";
        module.ModuleEntryAssemblyFileName = "original.dll";
        module.Status = ModuleStatus.Running;
        Directory.CreateDirectory(module.StoragePath);
        await File.WriteAllTextAsync(Path.Combine(module.StoragePath, "original.dll"), "original");
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        var originalPath = module.StoragePath;
        var failure = new InvalidOperationException("Simulated " + failureStage + " failure.");

        switch (failureStage)
        {
            case "scan": _endpointScanner.ExceptionToThrow = failure; break;
            case "build": _dockerService.BuildException = failure; break;
            case "start": _dockerService.StartException = failure; break;
            case "readiness": _dockerService.ReadinessException = failure; break;
            case "save": _saveInterceptor.ExceptionToThrow = failure; break;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.UpdateModuleFilesAsync(module.Id, new UpdateModuleFilesDto { ModuleFile = CreateDummyZipFile() }));

        Assert.Same(failure, exception);
        Assert.Equal("existing-container", module.ContainerId);
        Assert.Equal(originalPath, module.StoragePath);
        Assert.Equal("original.dll", module.ModuleEntryAssemblyFileName);
        Assert.Equal(ModuleStatus.Running, module.Status);
        Assert.True(File.Exists(Path.Combine(originalPath, "original.dll")));
        Assert.DoesNotContain("existing-container", _dockerService.RemovedContainerIds);
        Assert.False(Directory.Exists(_endpointScanner.ScannedDirectory));
        var persisted = await _context.Modules.AsNoTracking().SingleAsync(item => item.Id == module.Id);
        Assert.Equal("existing-container", persisted.ContainerId);
        Assert.Equal(originalPath, persisted.StoragePath);
        if (failureStage is "start" or "readiness" or "save")
            Assert.Contains("replacement-container", _dockerService.RemovedContainerIds);
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_EndpointContractChanges_PreservesActiveVersion()
    {
        var module = CreateTestModuleEntity("Contract", "api/v1/contract", 8021);
        module.ContainerId = "existing-container";
        module.Status = ModuleStatus.Running;
        module.SubEndpoints.Add(new ModuleEndpoint
        {
            ModuleId = module.Id, HttpMethod = "GET", EndpointPath = "/original"
        });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _endpointScanner.DiscoveredEndpoints = [new DiscoveredEndpointDto { HttpMethod = "GET", EndpointPath = "/changed" }];

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.UpdateModuleFilesAsync(module.Id, new UpdateModuleFilesDto { ModuleFile = CreateDummyZipFile() }));

        Assert.Empty(_dockerService.Calls);
        Assert.Equal("existing-container", module.ContainerId);
        Assert.Equal("/original", Assert.Single(module.SubEndpoints).EndpointPath);
        Assert.False(Directory.Exists(_endpointScanner.ScannedDirectory));
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
                .SetProperty(item => item.StoragePath, "winning-directory"));
        };

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            _sut.UpdateModuleFilesAsync(module.Id, new UpdateModuleFilesDto { ModuleFile = CreateDummyZipFile() }));

        var persisted = await _context.Modules.AsNoTracking().SingleAsync(item => item.Id == module.Id);
        Assert.Equal("winning-container", persisted.ContainerId);
        Assert.Equal("winning-directory", persisted.StoragePath);
        Assert.Equal(["replacement-container"], _dockerService.RemovedContainerIds);
        Assert.False(Directory.Exists(_dockerService.BuiltStoragePath));
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_Cancellation_CleansCandidateWithIndependentToken()
    {
        var module = CreateTestModuleEntity("Canceled", "api/v1/canceled", 8023);
        module.ContainerId = "existing-container";
        module.Status = ModuleStatus.Running;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        _dockerService.BeforeReady = () =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.UpdateModuleFilesAsync(module.Id, new UpdateModuleFilesDto { ModuleFile = CreateDummyZipFile() }, cancellation.Token));

        Assert.Equal("existing-container", module.ContainerId);
        Assert.Equal(["replacement-container"], _dockerService.RemovedContainerIds);
        Assert.False(Directory.Exists(_dockerService.BuiltStoragePath));
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_CleanupFails_KeepsNewVersionActive()
    {
        var module = CreateTestModuleEntity("Cleanup", "api/v1/cleanup", 8024);
        module.ContainerId = "existing-container";
        module.Status = ModuleStatus.Running;
        module.StoragePath = Path.Combine(_testRootDirectory, "retained-version");
        Directory.CreateDirectory(module.StoragePath);
        var previousPath = module.StoragePath;
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _dockerService.RemoveException = new InvalidOperationException("Cleanup failed.");

        await _sut.UpdateModuleFilesAsync(module.Id, new UpdateModuleFilesDto { ModuleFile = CreateDummyZipFile() });

        Assert.Equal("replacement-container", module.ContainerId);
        Assert.True(Directory.Exists(module.StoragePath));
        Assert.True(Directory.Exists(previousPath));
        var persisted = await _context.Modules.AsNoTracking().SingleAsync(item => item.Id == module.Id);
        Assert.Equal("replacement-container", persisted.ContainerId);
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

    [Fact]
    [Trait("Feature", "DeleteModule")]
    public async Task DeleteModuleAsync_ModuleExists_DeletesStorageDirectoryAndRemovesEntityWithEndpoints()
    {
        // Arrange
        var moduleDir = Path.Combine(_testRootDirectory, "module_to_delete");
        Directory.CreateDirectory(moduleDir);
        File.WriteAllText(Path.Combine(moduleDir, "test.txt"), "delete me");

        var module = CreateTestModuleEntity("To Delete", "api/v1/delete", 8009);
        module.StoragePath = moduleDir;

        var endpoint = new ModuleEndpoint
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
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
        Assert.Null(await _context.Modules.FindAsync(module.Id));
        Assert.Null(await _context.ModuleEndpoints.FindAsync(endpoint.Id));
    }

    #endregion

    #region Test Fakes & Helpers

    private static Module CreateTestModuleEntity(string name, string basePath, int port) => new()
    {
        Id = Guid.NewGuid(),
        ModuleName = name,
        Description = "Description for " + name,
        BaseEndpointPath = basePath.TrimStart('/'),
        ContainerPort = port,
        StoragePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
        ModuleEntryAssemblyFileName = "module.dll",
        Status = ModuleStatus.Created,
        CreatedAtUtc = DateTime.UtcNow
    };

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

    private sealed class FakeHostEnvironment(string rootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Server.Tests";
        public string ContentRootPath { get; set; } = rootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class FakeModuleEndpointScanner : IModuleEndpointScanner
    {
        public List<DiscoveredEndpointDto> DiscoveredEndpoints { get; set; } = [];

        public Exception? ExceptionToThrow { get; set; }

        public string? ScannedDirectory { get; private set; }

        public Task<ModuleScanResultDto> ScanDirectoryAsync(string moduleDirectoryPath, CancellationToken ct = default)
        {
            ScannedDirectory = moduleDirectoryPath;
            ct.ThrowIfCancellationRequested();

            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return Task.FromResult(new ModuleScanResultDto
            {
                EntryAssemblyFileName = "module.dll",
                DiscoveredEndpoints = DiscoveredEndpoints
            });
        }
    }

    private sealed class FakeDockerService : IDockerService
    {
        public List<string> RemovedContainerIds { get; } = [];
        public List<string> Calls { get; } = [];
        public string? BuiltStoragePath { get; private set; }
        public Exception? BuildException { get; set; }
        public Exception? StartException { get; set; }
        public Exception? ReadinessException { get; set; }
        public Exception? RemoveException { get; set; }
        public Func<Task>? BeforeReady { get; set; }
        public Func<string, Task>? BeforeRemove { get; set; }

        public Task<string> BuildContainerAsync(Guid moduleId, string storagePath, string entryDllName, int containerPort, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add("build");
            BuiltStoragePath = storagePath;
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
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public Exception? ExceptionToThrow { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return ValueTask.FromResult(result);
        }
    }

    #endregion
}