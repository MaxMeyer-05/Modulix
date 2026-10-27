using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;
using Modulix.Database.Entities;
using Modulix.Services;
using Modulix.Tests.TestData;

namespace Modulix.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ModuleVersionService"/>.
/// </summary>
[Trait("Category", "Services")]
[Trait("SubCategory", "ModuleVersionService")]
public class ModuleVersionServiceTests : ServiceTestBase
{
    private readonly ScannerService _scannerService;
    private readonly ModuleVersionService _sut;

    public ModuleVersionServiceTests()
    {
        _scannerService = new ScannerService(
            _context,
            NullLogger<ScannerService>.Instance,
            _endpointScanner,
            _moduleEndpointExtension);

        _sut = new ModuleVersionService(
            _context,
            NullLogger<ModuleVersionService>.Instance,
            _dockerService,
            _endpointScanner,
            _directoryExtension,
            _moduleEndpointExtension);
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_ModuleNotFound_ThrowsKeyNotFoundException()
    {
        var nonExistentId = Guid.NewGuid();
        var dto = new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile() };

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.CreateModuleVersionAsync(nonExistentId, dto));

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
            _sut.CreateModuleVersionAsync(module.Id, new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = upload }));

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
            _sut.CreateModuleVersionAsync(module.Id, new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile() }));

        Assert.Equal("Module endpoints must be confirmed before updating module files.", exception.Message);
        Assert.Empty(_dockerService.Calls);
        Assert.Null(_endpointScanner.ScannedDirectory);
        Assert.Equal("existing-container", module.ContainerId);
    }

    [Fact]
    [Trait("Feature", "UpdateModuleFiles")]
    public async Task UpdateModuleFilesAsync_ValidZip_ReplacesDirectoryContents()
    {
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

        var result = await _sut.CreateModuleVersionAsync(module.Id, dto);

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
            _sut.CreateModuleVersionAsync(module.Id, new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile() }));

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

        var result = await _sut.CreateModuleVersionAsync(module.Id,
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

        var result = await _sut.CreateModuleVersionAsync(module.Id,
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
        var persisted = await _context.Modules.AsNoTracking().Include(m => m.Versions).SingleAsync(item => item.Id == module.Id);
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
            ModuleId = module.Id,
            ModuleVersion = module.Versions.Single(),
            HttpMethod = "GET",
            EndpointPath = "/original",
            Status = ModuleEndpointsStatus.Active
        });
        _context.Modules.Add(module);
        await _context.SaveChangesAsync();
        _endpointScanner.DiscoveredEndpoints = [new DiscoveredSubEndpointDto { HttpMethod = "GET", EndpointPath = "/changed" }];

        var result = await _sut.CreateModuleVersionAsync(module.Id,
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
            _sut.CreateModuleVersionAsync(module.Id, new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile() }));

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
            _sut.CreateModuleVersionAsync(module.Id, new ModuleVersionCreateDto { Version = "v1.1.0", ModuleFile = CreateDummyZipFile() }, cancellation.Token));

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

        var result = await _sut.CreateModuleVersionAsync(module.Id,
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
}
