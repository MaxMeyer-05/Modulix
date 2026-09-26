using System.Text;
using System.IO.Compression;

using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

using Server.Database.Entities;
using Server.Database.DbContexts;

using Server.Models.Dtos;
using Server.Models.Enums;

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
    private readonly ModuleService _sut;

    #region Setup & Teardown

    public ModuleServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ServerContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ServerContext(options);
        _context.Database.EnsureCreated();

        _testRootDirectory = Path.Combine(Path.GetTempPath(), "Modulix_Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRootDirectory);

        var fakeEnv = new FakeHostEnvironment(_testRootDirectory);

        _sut = new ModuleService(
            _context,
            NullLogger<ModuleService>.Instance,
            fakeEnv);
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

        // Assert - Database state
        var persisted = await _context.Modules.FindAsync(result.Module.Id);
        Assert.NotNull(persisted);
        Assert.Equal("api/v1/analytics", persisted.BaseEndpointPath);
        Assert.Equal(result.Module.StoragePath, persisted.StoragePath);
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
                    MissingEndpoints = [new ModuleEndpointDto { Id = missingEndpoint.Id }],
                    ExtraEndpoints = [new ModuleEndpointDto { Id = extraEndpoint.Id }]
                }
            ]
        };

        // Act
        var result = await _sut.ConfirmEndpointsAsync(module.Id, confirmDto);

        // Assert - Return DTO
        Assert.Equal(ModuleStatus.Created, result.Status);

        // Assert - Database state
        var updatedMissing = await _context.ModuleEndpoints.FindAsync(missingEndpoint.Id);
        Assert.NotNull(updatedMissing);
        Assert.Equal(ModuleEndpointsStatus.Active, updatedMissing.Status);

        var removedExtra = await _context.ModuleEndpoints.FindAsync(extraEndpoint.Id);
        Assert.Null(removedExtra);
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
        Assert.Single(result.SubEndpoints);
        Assert.Equal("/prometheus", result.SubEndpoints.First().EndpointPath);
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
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a valid zip archive"));
        var corruptedZip = new FormFile(stream, 0, stream.Length, "ModuleFile", "archive.zip");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _sut.UpdateModuleFilesAsync(module.Id, new UpdateModuleFilesDto { ModuleFile = corruptedZip }));

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
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();

        const string newFileName = "updated_assembly.dll";
        var newZip = CreateDummyZipFile("new_release.zip", newFileName);
        var dto = new UpdateModuleFilesDto { ModuleFile = newZip };

        // Act
        await _sut.UpdateModuleFilesAsync(module.Id, dto);

        // Assert
        Assert.False(File.Exists(Path.Combine(moduleDir, "old_file.txt")));
        Assert.True(File.Exists(Path.Combine(moduleDir, newFileName)));
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

    private sealed class FakeHostEnvironment(string rootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Server.Tests";
        public string ContentRootPath { get; set; } = rootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    #endregion
}