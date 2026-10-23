using Microsoft.EntityFrameworkCore;

using Modulix.Mappers;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;

using Modulix.Client.Models.Enums;

using Modulix.Extensions;
using Modulix.Docker.Services;
using Modulix.Database.DbContexts;
using Modulix.Scanner.Interfaces;
using Modulix.Services.Interfaces;

namespace Modulix.Services;

/// <inheritdoc cref="IModuleVersionService"/>
public class ModuleVersionService : IModuleVersionService
{
    /// <summary>
    /// The database context used by the service.
    /// </summary>
    private readonly ServerContext _context;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<ModuleVersionService> _logger;

    /// <summary>
    /// The module endpoint scanner.
    /// </summary>
    private readonly IModuleEndpointScanner _endpointScanner;

    /// <summary>
    /// The Docker service.
    /// </summary>
    private readonly IDockerService _dockerService;

    /// <summary>
    /// The directory extension for handling module directories and archives.
    /// </summary>
    private readonly DirectoryExtension _directoryExtension;

    /// <summary>
    /// The module endpoint extension for validating and reconciling endpoints.
    /// </summary>
    private readonly ModuleEndpointExtension _moduleEndpointExtension;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleVersionService"/> class.
    /// </summary>
    /// <param name="context">The database context used by the service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="dockerService">The Docker service.</param>
    /// <param name="endpointScanner">The module endpoint scanner.</param>
    /// <param name="directoryExtension">The directory extension for handling module directories and archives.</param>
    /// <param name="moduleEndpointExtension">The extension for validating and reconciling module endpoints.</param>
    public ModuleVersionService(
        ServerContext context,
        ILogger<ModuleVersionService> logger,
        IDockerService dockerService,
        IModuleEndpointScanner endpointScanner,
        DirectoryExtension directoryExtension,
        ModuleEndpointExtension moduleEndpointExtension)
    {
        _context = context;
        _logger = logger;
        _dockerService = dockerService;
        _endpointScanner = endpointScanner;
        _directoryExtension = directoryExtension;
        _moduleEndpointExtension = moduleEndpointExtension;
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<ModuleVersionDto>> GetAllModuleVersionsAsync(ModuleVersionQueryDto? moduleVersionQuery = null, PaginationQueryDto? pagination = null, CancellationToken ct = default)
    {
        pagination ??= new PaginationQueryDto();
        var page = pagination.Page < 1 ? PaginationQueryDto.DefaultPage : pagination.Page;
        var pageSize = pagination.PageSize < 1
            ? PaginationQueryDto.DefaultPageSize
            : Math.Min(pagination.PageSize, PaginationQueryDto.MaxPageSize);

        var query = _context.ModuleVersions.AsNoTracking();
        if (moduleVersionQuery is not null)
        {
            if (moduleVersionQuery.Version is not null)
            {
                query = query.Where(v => v.Version == moduleVersionQuery.Version);
            }
        }

        var totalCount = await query.CountAsync(ct);
        var versions = await query
            .OrderByDescending(v => v.ReleasedAtUtc)
            .ThenByDescending(v => v.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var versionDtos = versions.Select(v => v.ToVersionDto()).ToList();

        _logger.LogDebug("Retrieved {Count} module versions from the database.", versionDtos.Count);

        return new PagedResultDto<ModuleVersionDto>
        {
            Items = versionDtos,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ModuleVersionDto>> GetModuleVersionsByModuleIdAsync(Guid moduleId, CancellationToken ct = default)
    {
        var exists = await _context.Modules.FindAsync([moduleId], ct);
        if (exists is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        var versions = await _context.ModuleVersions
            .AsNoTracking()
            .Where(v => v.ModuleId == moduleId)
            .ToListAsync(ct);

        var versionDtos = versions.Select(v => v.ToVersionDto()).ToList();

        _logger.LogDebug("Retrieved {Count} versions for module with ID '{ModuleId}'.", versionDtos.Count, moduleId);
        return versionDtos;
    }

    /// <inheritdoc/>
    public async Task<ModuleVersionDto> GetModuleVersionByIdAsync(Guid moduleId, string version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Module version cannot be empty.");

        var normalizedVersion = version.Trim();
        var exists = await _context.Modules.FindAsync([moduleId], ct);
        if (exists is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        var moduleVersion = await _context.ModuleVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(v => v.ModuleId == moduleId && v.Version == normalizedVersion, ct);

        if (moduleVersion is null)
            throw new KeyNotFoundException($"Version '{normalizedVersion}' for module with ID '{moduleId}' was not found.");

        return moduleVersion.ToVersionDto();
    }

    /// <inheritdoc/>
    public async Task<ModuleCreationResultDto> CreateModuleVersionAsync(Guid moduleId, ModuleVersionCreateDto dto, CancellationToken ct = default)
    {
        var module = await _context.Modules
            .Include(m => m.SubEndpoints)
            .Include(m => m.Versions)
            .SingleOrDefaultAsync(m => m.Id == moduleId, ct);

        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        if (string.IsNullOrWhiteSpace(dto.Version))
            throw new ArgumentException("Module version cannot be empty.");

        var normalizedVersion = dto.Version.Trim();
        if (module.Versions.Any(v => v.Version.Equals(normalizedVersion, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Version '{normalizedVersion}' already exists for module '{moduleId}'.");

        if (!dto.ModuleFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The module file must be a ZIP archive.");

        if (module.Status == ModuleStatus.PendingConfirmation ||
            module.SubEndpoints.Any(endpoint => endpoint.Status == ModuleEndpointsStatus.PendingConfirmation))
        {
            throw new InvalidOperationException("Module endpoints must be confirmed before updating module files.");
        }

        var previousState = (module.ContainerId, module.CurrentVersion, module.Status);
        var versionDirectory = await _directoryExtension.CreateModuleTargetPathAsync(module.Id, normalizedVersion, dto.ModuleFile, ct);
        var newVersion = dto.ToVersionEntity();
        newVersion.ModuleId = module.Id;
        newVersion.StoragePath = versionDirectory;
        SubEndpointDiscrepancyReportDto? report = null;

        try
        {
            ct.ThrowIfCancellationRequested();

            var scanResponse = await _endpointScanner.ScanOrEnqueueAsync(module.Id, versionDirectory, ScanPriority.Patch, ct);
            module.CurrentVersion = normalizedVersion;
            module.Versions.Add(newVersion);

            if (scanResponse.WasQueued)
            {
                newVersion.ModuleEntryAssemblyFileName = string.Empty;
                _logger.LogInformation("Module scan for module with ID '{ModuleId}' was queued.", moduleId);
                module.Status = ModuleStatus.QueuedForScan;
                await _context.SaveChangesAsync(ct);
                _moduleEndpointExtension.SynchronizeReverseProxy(module);

                return new ModuleCreationResultDto
                {
                    Module = module.ToModuleDetailDto(),
                    Message = "Alle Scanner-Container sind besetzt. Das Update wurde in die Warteschlange aufgenommen."
                };
            }

            newVersion.ModuleEntryAssemblyFileName = scanResponse.Result!.EntryAssemblyFileName;
            report = await _moduleEndpointExtension.CreateEndpointDiscrepancyReportAsync(module, scanResponse.Result.DiscoveredEndpoints, ct);
        }
        catch (Exception ex)
        {
            if (module.ContainerId != previousState.ContainerId && !string.IsNullOrWhiteSpace(module.ContainerId))
            {
                await _dockerService.TryRemoveModuleContainerAsync(module.ContainerId, module.Id);
            }

            var autoDetectChanges = _context.ChangeTracker.AutoDetectChangesEnabled;
            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
                (module.ContainerId, module.CurrentVersion, module.Status) = previousState;
                module.Versions.Remove(newVersion);
                _context.Entry(newVersion).State = EntityState.Detached;
                _context.Entry(module).State = EntityState.Unchanged;
            }
            finally
            {
                _context.ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
            }
            _directoryExtension.DeleteModuleDirectory(versionDirectory, module.Id);

            _logger.LogError(ex, "An error occurred while creating version '{Version}' for module with ID '{ModuleId}'.", normalizedVersion, moduleId);
            throw;
        }

        // Remove old container when the new version is running; keep previous version directory for rollbacks
        if (module.Status == ModuleStatus.Running &&
            !string.IsNullOrWhiteSpace(previousState.ContainerId) &&
            previousState.ContainerId != module.ContainerId)
        {
            await _dockerService.TryRemoveModuleContainerAsync(previousState.ContainerId, module.Id);
        }

        _logger.LogInformation("Created version '{Version}' for module '{ModuleId}' (Status: {Status}).", normalizedVersion, moduleId, module.Status);
        return new ModuleCreationResultDto
        {
            Module = module.ToModuleDetailDto(),
            DiscrepancyReport = report,
            Message = report?.HasDiscrepancy == true
                ? "Endpoints require confirmation before the updated container is activated."
                : "Replacement container activated successfully."
        };
    }

    /// <inheritdoc/>
    public async Task<ModuleDetailDto> RollbackModuleVersionAsync(Guid moduleId, string version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Module version cannot be empty.");

        var normalizedVersion = version.Trim();
        var module = await _context.Modules
            .Include(m => m.SubEndpoints)
            .Include(m => m.Versions)
            .SingleOrDefaultAsync(m => m.Id == moduleId, ct);

        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        if (module.Status == ModuleStatus.QueuedForScan)
            throw new InvalidOperationException($"Module '{moduleId}' cannot be rolled back while queued for scanning.");

        if (module.Status == ModuleStatus.PendingConfirmation ||
            module.SubEndpoints.Any(endpoint => endpoint.Status == ModuleEndpointsStatus.PendingConfirmation))
        {
            throw new InvalidOperationException("Module endpoints must be confirmed before rolling back the module.");
        }

        var targetVersion = module.Versions.FirstOrDefault(v => v.Version == normalizedVersion);
        if (targetVersion is null)
            throw new KeyNotFoundException($"Version '{normalizedVersion}' for module with ID '{moduleId}' was not found.");

        if (string.Equals(module.CurrentVersion, targetVersion.Version, StringComparison.Ordinal))
            throw new InvalidOperationException($"Module '{moduleId}' is already on version '{targetVersion.Version}'.");

        var previousState = (module.ContainerId, module.CurrentVersion, module.Status);
        var previousEndpointStatuses = module.SubEndpoints
            .Select(endpoint => (Endpoint: endpoint, endpoint.Status))
            .ToList();
        string? builtContainerId = null;

        foreach (var endpoint in module.SubEndpoints)
        {
            var endpointVersion = endpoint.ModuleVersion
                ?? module.Versions.FirstOrDefault(v => v.Id == endpoint.ModuleVersionId);

            var existedInTargetVersion = endpointVersion is not null &&
                (endpointVersion.ReleasedAtUtc < targetVersion.ReleasedAtUtc ||
                 (endpointVersion.ReleasedAtUtc == targetVersion.ReleasedAtUtc && endpointVersion.Id <= targetVersion.Id));

            endpoint.Status = existedInTargetVersion
                ? ModuleEndpointsStatus.Active
                : ModuleEndpointsStatus.Inactive;
        }

        try
        {
            module.CurrentVersion = targetVersion.Version;
            module.Status = ModuleStatus.Starting;

            builtContainerId = await _dockerService.BuildContainerAsync(
                module.Id,
                targetVersion.StoragePath,
                targetVersion.ModuleEntryAssemblyFileName,
                module.ContainerPort,
                ct);
            module.ContainerId = builtContainerId;

            await _dockerService.RunContainerAsync(builtContainerId, ct);
            await _dockerService.WaitUntilReadyAsync(builtContainerId, ct);
            module.Status = ModuleStatus.Running;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to start rolled back container for module '{ModuleId}' (Version: '{Version}').", module.Id, targetVersion.Version);
            module.Status = ModuleStatus.Failed;
        }

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch
        {
            if (builtContainerId is not null)
                await _dockerService.TryRemoveModuleContainerAsync(builtContainerId, module.Id);

            (module.ContainerId, module.CurrentVersion, module.Status) = previousState;
            _context.Entry(module).State = EntityState.Unchanged;

            foreach (var previousEndpoint in previousEndpointStatuses)
            {
                previousEndpoint.Endpoint.Status = previousEndpoint.Status;
                _context.Entry(previousEndpoint.Endpoint).State = EntityState.Unchanged;
            }

            throw;
        }

        if (!string.IsNullOrWhiteSpace(previousState.ContainerId) &&
            previousState.ContainerId != module.ContainerId)
        {
            await _dockerService.TryRemoveModuleContainerAsync(previousState.ContainerId, module.Id);
        }

        _moduleEndpointExtension.SynchronizeReverseProxy(module);
        _logger.LogInformation("Rolled back module '{ModuleId}' to version '{Version}' (Status: {Status}).", moduleId, targetVersion.Version, module.Status);
        return module.ToModuleDetailDto();
    }

    /// <inheritdoc/>
    public async Task UpdateModuleVersionAsync(Guid moduleId, string version, ModuleVersionUpdateDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Module version cannot be empty.");

        var normalizedVersion = version.Trim();
        var module = await _context.Modules
            .Include(m => m.Versions)
            .SingleOrDefaultAsync(m => m.Id == moduleId, ct);

        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        var moduleVersion = module.Versions.FirstOrDefault(v => v.Version == normalizedVersion);
        if (moduleVersion is null)
            throw new KeyNotFoundException($"Version '{normalizedVersion}' for module with ID '{moduleId}' was not found.");

        if (dto.Version is not null)
        {
            var updatedVersionName = dto.Version.Trim();
            if (string.IsNullOrWhiteSpace(updatedVersionName))
                throw new ArgumentException("Updated module version cannot be empty.");

            if (!string.Equals(moduleVersion.Version, updatedVersionName, StringComparison.Ordinal) &&
                module.Versions.Any(v => v.Id != moduleVersion.Id && v.Version.Equals(updatedVersionName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Version '{updatedVersionName}' already exists for module '{moduleId}'.");
            }

            if (string.Equals(module.CurrentVersion, moduleVersion.Version, StringComparison.Ordinal))
            {
                module.CurrentVersion = updatedVersionName;
            }
        }

        moduleVersion.UpdateVersionEntity(dto);

        _context.ChangeTracker.DetectChanges();
        if (_context.Entry(moduleVersion).State == EntityState.Unchanged &&
            _context.Entry(module).State == EntityState.Unchanged)
        {
            return;
        }

        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Updated version '{Version}' for module with ID '{ModuleId}'.", moduleVersion.Version, moduleId);
    }

    /// <inheritdoc/>
    public async Task DeleteModuleVersionAsync(Guid moduleId, string version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Module version cannot be empty.");

        var normalizedVersion = version.Trim();
        var module = await _context.Modules
            .Include(m => m.Versions)
            .Include(m => m.SubEndpoints)
            .SingleOrDefaultAsync(m => m.Id == moduleId, ct);

        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        var moduleVersion = module.Versions.FirstOrDefault(v => v.Version == normalizedVersion);
        if (moduleVersion is null)
            throw new KeyNotFoundException($"Version '{normalizedVersion}' for module with ID '{moduleId}' was not found.");

        if (string.Equals(module.CurrentVersion, moduleVersion.Version, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cannot delete the currently active version '{moduleVersion.Version}' of module '{moduleId}'.");

        var nextVersion = module.Versions
            .Where(v => v.Id != moduleVersion.Id &&
                (v.ReleasedAtUtc > moduleVersion.ReleasedAtUtc ||
                 (v.ReleasedAtUtc == moduleVersion.ReleasedAtUtc && v.Id > moduleVersion.Id)))
            .OrderBy(v => v.ReleasedAtUtc)
            .ThenBy(v => v.Id)
            .FirstOrDefault();

        var discoveredEndpoints = module.SubEndpoints
            .Where(e => e.ModuleVersionId == moduleVersion.Id)
            .ToList();

        if (nextVersion is not null)
        {
            foreach (var endpoint in discoveredEndpoints)
            {
                endpoint.ModuleVersionId = nextVersion.Id;
                endpoint.ModuleVersion = nextVersion;
            }
        }
        else if (discoveredEndpoints.Count > 0)
        {
            _context.ModuleEndpoints.RemoveRange(discoveredEndpoints);
        }

        _directoryExtension.DeleteModuleDirectory(moduleVersion.StoragePath, moduleId);

        _context.ModuleVersions.Remove(moduleVersion);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted version '{Version}' for module with ID '{ModuleId}'.", normalizedVersion, moduleId);
    }
}