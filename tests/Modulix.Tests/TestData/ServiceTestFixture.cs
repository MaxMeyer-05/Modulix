using System.Buffers.Binary;
using System.IO.Compression;

using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;
using Modulix.Database.DbContexts;
using Modulix.Database.Entities;
using Modulix.Docker.Models;
using Modulix.Docker.Services.Interfaces;
using Modulix.Extensions;
using Modulix.ReverseProxy.Services;
using Modulix.Scanner.Interfaces;

namespace Modulix.Tests.TestData;

/// <summary>
/// Shared SQLite in-memory database, temporary storage directory, and test doubles for service unit tests.
/// </summary>
public abstract class ServiceTestBase : IDisposable
{
    protected readonly SqliteConnection _connection;
    protected readonly ServerContext _context;
    protected readonly string _testRootDirectory;
    protected readonly FakeModuleEndpointScanner _endpointScanner;
    protected readonly FakeDockerService _dockerService;
    protected readonly ReverseProxyService _reverseProxyService;
    protected readonly FailingSaveInterceptor _saveInterceptor = new();
    protected readonly DirectoryExtension _directoryExtension;
    protected readonly ModuleEndpointExtension _moduleEndpointExtension;
    protected readonly ModuleVersionExtension _moduleVersionExtension;
    protected readonly IOptions<ModuleSettings> _moduleOptions;

    protected ServiceTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ServerContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_saveInterceptor)
            .Options;

        _context = new ServerContext(options);
        _context.Database.EnsureCreated();

        _testRootDirectory = Path.Combine(Path.GetTempPath(), "Modulix_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRootDirectory);

        _moduleOptions = Options.Create(new ModuleSettings
        {
            StorageBasePath = Path.Combine(_testRootDirectory, "storage", "modules")
        });

        _endpointScanner = new FakeModuleEndpointScanner();
        _dockerService = new FakeDockerService();
        _reverseProxyService = new ReverseProxyService(NullLogger<ReverseProxyService>.Instance);
        _directoryExtension = new DirectoryExtension(NullLogger<DirectoryExtension>.Instance, _moduleOptions);
        _moduleVersionExtension = new ModuleVersionExtension(_moduleOptions, _context, NullLogger<ModuleVersionExtension>.Instance);
        _moduleEndpointExtension = new ModuleEndpointExtension(
            _context,
            NullLogger<ModuleEndpointExtension>.Instance,
            _dockerService,
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
                // Ignored during test cleanup
            }
        }

        GC.SuppressFinalize(this);
    }

    protected static Module CreateTestModuleEntity(string name, string basePath, int port)
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

    protected static IFormFile CreateDummyZipFile(string fileName = "module.zip", string containedEntryName = "service.dll")
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

    protected static IFormFile CreateZipFileWithEntries(int entryCount)
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

    protected static IFormFile CreateZipFileWithClaimedUncompressedSize(uint uncompressedSize)
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

    protected void AssertNoStoredModuleDirectories()
    {
        var modulesStorageDirectory = Path.Combine(_testRootDirectory, "storage", "modules");
        Assert.True(Directory.Exists(modulesStorageDirectory));
        Assert.Empty(Directory.EnumerateDirectories(modulesStorageDirectory));
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
}

/// <summary>
/// Configurable in-memory test double for <see cref="IModuleEndpointScanner"/>.
/// </summary>
public sealed class FakeModuleEndpointScanner : IModuleEndpointScanner
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
        Guid moduleId,
        string moduleDirectoryPath,
        ScanPriority priority = ScanPriority.Create,
        CancellationToken ct = default)
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

/// <summary>
/// Configurable in-memory test double for <see cref="IModuleDockerService"/>.
/// </summary>
public sealed class FakeDockerService : IModuleDockerService
{
    public List<string> RemovedContainerIds { get; } = [];
    public List<string> Calls { get; } = [];
    public string? BuiltStoragePath { get; private set; }
    public string? BuiltEntryAssembly { get; private set; }
    public bool IsContainerRunning { get; set; } = true;
    public string ContainerLogsResult { get; set; } = string.Empty;
    public ContainerStats ContainerStatsResult { get; set; } = new()
    {
        IsRunning = true,
        CpuUsagePercent = 12.5,
        MemoryUsageMiB = 64.0,
        MemoryLimitMiB = 512.0,
        UptimeSeconds = 120.0,
        ExitCode = 0
    };
    public Exception? BuildException { get; set; }
    public Exception? StartException { get; set; }
    public Exception? ReadinessException { get; set; }
    public Exception? RemoveException { get; set; }
    public Exception? LogsException { get; set; }
    public Func<Task>? BeforeReady { get; set; }
    public Func<string, Task>? BeforeRemove { get; set; }

    public Task<string> BuildModuleContainerAsync(Guid moduleId, string storagePath, string entryDllName, int containerPort, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Calls.Add("build");
        BuiltStoragePath = storagePath;
        BuiltEntryAssembly = entryDllName;
        if (BuildException is not null)
            throw BuildException;
        return Task.FromResult("replacement-container");
    }

    public Task RunModuleContainerAsync(string containerId, CancellationToken ct = default)
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

    public Task StopModuleContainerAsync(string containerId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Calls.Add("stop:" + containerId);
        return Task.CompletedTask;
    }

    public async Task RemoveModuleContainerAsync(string containerId, Guid containerName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Calls.Add("remove:" + containerId);
        RemovedContainerIds.Add(containerId);
        if (BeforeRemove is not null)
            await BeforeRemove(containerId);
        if (RemoveException is not null)
            throw RemoveException;
    }

    public async Task<bool> TryRemoveModuleContainerAsync(string containerId, Guid containerName, CancellationToken ct = default)
    {
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await RemoveModuleContainerAsync(containerId, containerName, cleanupCts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> IsModuleRunningAsync(string containerId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(IsContainerRunning);
    }

    public Task<ContainerStats> GetModuleStatsAsync(string containerId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Calls.Add($"stats:{containerId}");
        return Task.FromResult(ContainerStatsResult);
    }
}

/// <summary>
/// EF Core interceptor that simulates database save failures or pre-save callbacks.
/// </summary>
public sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    public Exception? ExceptionToThrow { get; set; }
    public Action? BeforeSave { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        BeforeSave?.Invoke();
        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        return ValueTask.FromResult(result);
    }
}
