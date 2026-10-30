using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Modulix.Mappers;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;

using Modulix.Client.Models.Enums;

using Modulix.Database.Entities;
using Modulix.Database.DbContexts;

using Modulix.Extensions;
using Modulix.ReverseProxy.Services;

using Modulix.Scanner.Interfaces;
using Modulix.Services.Interfaces;
using Modulix.Docker.Services.Interfaces;

namespace Modulix.Services;

/// <inheritdoc cref="IModuleService"/>
public class ModuleService : IModuleService
{
    /// <summary>
    /// The database context used by the service.
    /// </summary>
    private readonly ServerContext _context;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<ModuleService> _logger;

    /// <summary>
    /// The module endpoint scanner.
    /// </summary>
    private readonly IModuleEndpointScanner _endpointScanner;

    /// <summary>
    /// The Docker service.
    /// </summary>
    private readonly IModuleDockerService _dockerService;

    /// <summary>
    /// The shared module settings.
    /// </summary>
    private readonly ModuleSettings _options;

    /// <summary>
    /// The directory extension for handling module directories and archives.
    /// </summary>
    private readonly DirectoryExtension _directoryExtension;

    /// <summary>
    /// The module endpoint extension for validating and reconciling endpoints.
    /// </summary>
    private readonly ModuleEndpointExtension _moduleEndpointExtension;

    /// <summary>
    /// The reverse proxy service used to synchronize module routing configurations.
    /// </summary>
    private readonly IReverseProxyService _reverseProxyService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleService"/> class.
    /// </summary>
    /// <param name="context">The database context used by the service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="options">The shared module settings.</param>
    /// <param name="dockerService">The Docker service.</param>
    /// <param name="endpointScanner">The module endpoint scanner.</param>
    /// <param name="directoryExtension">The directory extension for handling module directories and archives.</param>
    /// <param name="moduleEndpointExtension">The extension for validating and reconciling module endpoints.</param>
    /// <param name="reverseProxyService">The reverse proxy service.</param>
    public ModuleService(
        ServerContext context, 
        ILogger<ModuleService> logger,
        IOptions<ModuleSettings> options,
        IModuleDockerService dockerService,
        IModuleEndpointScanner endpointScanner,
        DirectoryExtension directoryExtension,
        ModuleEndpointExtension moduleEndpointExtension,
        IReverseProxyService reverseProxyService)
    {
        _context = context;
        _logger = logger;
        _endpointScanner = endpointScanner;
        _dockerService = dockerService;
        _options = options.Value;
        _directoryExtension = directoryExtension;
        _moduleEndpointExtension = moduleEndpointExtension;
        _reverseProxyService = reverseProxyService;
    }
    
    /// <inheritdoc/>
    public async Task<ModuleDetailDto> StartModuleAsync(Guid moduleId, CancellationToken ct = default)
    {
        var module = await _context.Modules
            .Include(m => m.SubEndpoints)
            .Include(m => m.Versions)
            .SingleOrDefaultAsync(m => m.Id == moduleId, ct);

        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        if (module.Status == ModuleStatus.Running)
        {
            if (!string.IsNullOrWhiteSpace(module.ContainerId) &&
                await _dockerService.IsModuleRunningAsync(module.ContainerId, ct))
            {
                throw new InvalidOperationException($"Module '{moduleId}' is already running.");
            }

            module.Status = ModuleStatus.Failed;
            ModuleEndpointExtension.DeactivateActiveSubEndpoints(module);
            _reverseProxyService.RemoveModule(module.Id);
        }

        if (module.Status == ModuleStatus.Starting)
            throw new InvalidOperationException($"Module '{moduleId}' is already starting.");

        if (module.Status == ModuleStatus.QueuedForScan)
            throw new InvalidOperationException($"Module '{moduleId}' cannot be started while queued for scanning.");

        if (module.Status == ModuleStatus.PendingConfirmation ||
            module.SubEndpoints.Any(endpoint => endpoint.Status == ModuleEndpointsStatus.PendingConfirmation))
        {
            throw new InvalidOperationException("Module endpoints must be confirmed before starting the module.");
        }

        var previousState = (module.ContainerId, module.Status);
        var previousEndpointStatuses = module.SubEndpoints
            .Select(endpoint => (Endpoint: endpoint, endpoint.Status))
            .ToList();
        string? builtContainerId = null;

        try
        {
            _directoryExtension.EnsureModuleDataDirectory(module.Id);
            module.Status = ModuleStatus.Starting;

            if (string.IsNullOrWhiteSpace(module.ContainerId))
            {
                var currentVersion = module.Versions.FirstOrDefault(v => v.Version == module.CurrentVersion)
                    ?? throw new InvalidOperationException($"Storage path for module '{module.Id}' version '{module.CurrentVersion}' was not found.");

                builtContainerId = await _dockerService.BuildModuleContainerAsync(
                    module.Id,
                    currentVersion.StoragePath,
                    currentVersion.ModuleEntryAssemblyFileName,
                    module.ContainerPort,
                    ct);
                module.ContainerId = builtContainerId;
            }

            await _dockerService.RunModuleContainerAsync(module.ContainerId, ct);
            await _dockerService.WaitUntilReadyAsync(module.ContainerId, ct);
            module.Status = ModuleStatus.Running;
            ModuleEndpointExtension.ActivateCurrentVersionSubEndpoints(module);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to start module container for module '{ModuleId}'.", module.Id);
            module.Status = ModuleStatus.Failed;
            ModuleEndpointExtension.DeactivateActiveSubEndpoints(module);
        }

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch
        {
            if (builtContainerId is not null)
                await _dockerService.TryRemoveModuleContainerAsync(builtContainerId, module.Id);

            (module.ContainerId, module.Status) = previousState;
            _context.Entry(module).State = EntityState.Unchanged;

            foreach (var previousEndpoint in previousEndpointStatuses)
            {
                previousEndpoint.Endpoint.Status = previousEndpoint.Status;
                _context.Entry(previousEndpoint.Endpoint).State = EntityState.Unchanged;
            }

            throw;
        }

        _moduleEndpointExtension.SynchronizeReverseProxy(module);
        _logger.LogInformation("Started module with ID '{ModuleId}' (Status: {Status}).", moduleId, module.Status);
        return module.ToModuleDetailDto();
    }

    /// <inheritdoc/>
    public async Task<ModuleDetailDto> StopModuleAsync(Guid moduleId, CancellationToken ct = default)
    {
        var module = await _context.Modules
            .Include(m => m.SubEndpoints)
            .Include(m => m.Versions)
            .SingleOrDefaultAsync(m => m.Id == moduleId, ct);

        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        if (module.Status == ModuleStatus.Stopped)
            throw new InvalidOperationException($"Module '{moduleId}' is already stopped.");

        if (module.Status is not ModuleStatus.Running and not ModuleStatus.Starting)
            throw new InvalidOperationException($"Module '{moduleId}' is not running and cannot be stopped.");

        if (!string.IsNullOrWhiteSpace(module.ContainerId))
        {
            await _dockerService.StopModuleContainerAsync(module.ContainerId, ct);
        }

        module.Status = ModuleStatus.Stopped;
        ModuleEndpointExtension.DeactivateActiveSubEndpoints(module);
        await _context.SaveChangesAsync(ct);

        _moduleEndpointExtension.SynchronizeReverseProxy(module);
        _logger.LogInformation("Stopped module with ID '{ModuleId}'.", moduleId);
        return module.ToModuleDetailDto();
    }

    /// <inheritdoc/>
    public async Task<ModuleCreationResultDto> CreateModuleAsync(CreateModuleDto dto, CancellationToken ct = default)
    {
        // Normalize the base endpoint path
        if (string.IsNullOrWhiteSpace(dto.BaseUrlPath))
            throw new ArgumentException("Base endpoint path cannot be empty.");
        var normalizedBasePath = dto.BaseUrlPath.Trim().TrimEnd('/').TrimStart('/');

        if (string.IsNullOrWhiteSpace(dto.Version))
            throw new ArgumentException("Module version cannot be empty.");
        var normalizedVersion = dto.Version.Trim();
        
        // Ensure the used container ports are not conflicting
        var usedPorts = await _context.Modules.Select(m => m.ContainerPort).ToListAsync(ct);
        var availablePort = PortExtension.GetAvailablePort(usedPorts, dto.ContainerPort ?? 0);

        // Ensure the used base endpoint paths are not conflicting
        var usedBasePaths = await _context.Modules.Select(m => m.BaseUrlPath).ToListAsync(ct);
        ModuleEndpointExtension.CheckBaseUrlPathConflict(usedBasePaths, normalizedBasePath);

        // Create the module entity and set the available container port
        var module = dto.ToModuleEntity();
        module.BaseUrlPath = normalizedBasePath;
        module.ContainerPort = availablePort;
        module.CurrentVersion = normalizedVersion;

        // Prepare the target path for extracting the module file
        var targetPath = await _directoryExtension.CreateModuleTargetPathAsync(module.Id, normalizedVersion, dto.ModuleFile, ct);

        // Create the initial version entry for the module
        var initialVersion = new ModuleVersion
        {
            ModuleId = module.Id,
            Version = normalizedVersion,
            Changelog = dto.Changelog?.Trim(),
            StoragePath = targetPath
        };
        module.Versions.Add(initialVersion);

        // Add initial sub-endpoints for the module
        var manualRoutes = dto.InitialEndpoints?
            .Where(ep => ep.HttpMethod is not null && !string.IsNullOrWhiteSpace(ep.EndpointPath))
            .Select(ep =>
            {
                var path = ep.EndpointPath.Trim();
                if (!path.StartsWith('/'))
                    path = "/" + path;

                return new CreateModuleSubEndpointDto
                {
                    HttpMethod = ep.HttpMethod,
                    EndpointPath = path
                };
            })
            .DistinctBy(ep => (ep.HttpMethod, ep.EndpointPath))
            .ToList() ?? [];

        foreach (var ep in manualRoutes)
        {
            module.SubEndpoints.Add(new ModuleEndpoint
            {
                ModuleId = module.Id,
                ModuleVersion = initialVersion,
                HttpMethod = ep.HttpMethod,
                EndpointPath = ep.EndpointPath,
                Status = ModuleEndpointsStatus.PendingConfirmation 
            });
        }

        // Scan the module for endpoints and handle any errors during the scanning process
        ScanEnqueueResponse scanResponse;
        try
        {
            scanResponse = await _endpointScanner.ScanOrEnqueueAsync(module.Id, initialVersion.StoragePath, ScanPriority.Create, ct);
            initialVersion.ModuleEntryAssemblyFileName = scanResponse.WasQueued
                ? string.Empty
                : (scanResponse.Result ?? throw new InvalidOperationException("The scanner did not return a scan result.")).EntryAssemblyFileName;
        }
        catch
        {
            _directoryExtension.DeleteModuleDirectory(initialVersion.StoragePath, module.Id);
            _directoryExtension.DeleteModuleDataDirectory(module.Id);
            throw;
        }

        SubEndpointDiscrepancyReportDto? discrepancyReport = null;
        // Log the final status of the module after attempting to provision the Docker container
        try
        {
            _context.Modules.Add(module);
            if (scanResponse.WasQueued)
            {
                module.Status = ModuleStatus.QueuedForScan;
                ModuleEndpointExtension.DeactivateActiveSubEndpoints(module);
                await _context.SaveChangesAsync(ct);
            }
            else
            {
                discrepancyReport = await _moduleEndpointExtension.CreateEndpointDiscrepancyReportAsync(module, scanResponse.Result!.DiscoveredEndpoints, ct);
            }
        }
        catch
        {
            if (scanResponse.WasQueued)
                _endpointScanner.CancelScan(module.Id);
            if (string.IsNullOrWhiteSpace(module.ContainerId) ||
                await _dockerService.TryRemoveModuleContainerAsync(module.ContainerId, module.Id))
            {
                _directoryExtension.DeleteModuleDirectory(initialVersion.StoragePath, module.Id);
                _directoryExtension.DeleteModuleDataDirectory(module.Id);
            }

            _context.Entry(module).State = EntityState.Detached;
            foreach (var version in _context.ChangeTracker.Entries<ModuleVersion>()
                .Where(entry => entry.Entity.ModuleId == module.Id).ToList())
            {
                version.State = EntityState.Detached;
            }
            foreach (var endpoint in _context.ChangeTracker.Entries<ModuleEndpoint>()
                .Where(entry => entry.Entity.ModuleId == module.Id).ToList())
            {
                endpoint.State = EntityState.Detached;
            }

            throw;
        }

        _logger.LogInformation("Created module with ID '{ModuleId}' (Version: '{Version}') and stored its files at '{StoragePath}'.", module.Id, initialVersion.Version, initialVersion.StoragePath);

        return new ModuleCreationResultDto
        {
            Module = module.ToModuleDetailDto(),
            DiscrepancyReport = scanResponse.WasQueued ? null : discrepancyReport ?? new SubEndpointDiscrepancyReportDto
            {
                ModuleId = module.Id,
                MatchedEndpoints = manualRoutes.Count == 0 ? null : manualRoutes.Select(endpoint => new DiscoveredSubEndpointDto
                {
                    HttpMethod = endpoint.HttpMethod,
                    EndpointPath = endpoint.EndpointPath
                }).ToList()
            },
            Message = scanResponse.WasQueued ? "Scan was queued." : "Scan completed."
        };
    }

    /// <inheritdoc/>
    public async Task DeleteModuleAsync(Guid moduleId, CancellationToken ct = default)
    {
        var module = await _context.Modules
            .Include(m => m.Versions)
            .Include(m => m.SubEndpoints)
            .SingleOrDefaultAsync(m => m.Id == moduleId, ct);
        if (module is null)
            return;

        if (module.Status == ModuleStatus.QueuedForScan)
        {
            _endpointScanner.CancelScan(moduleId);
        }

        if (!string.IsNullOrEmpty(module.ContainerId))
        {
            await _dockerService.RemoveModuleContainerAsync(module.ContainerId, module.Id, ct);
        }

        foreach (var version in module.Versions)
        {
            _directoryExtension.DeleteModuleDirectory(version.StoragePath, moduleId);
        }

        _directoryExtension.DeleteModuleDataDirectory(moduleId);

        var moduleBaseDirectory = Path.Combine(_options.StorageBasePath, moduleId.ToString());
        _directoryExtension.DeleteModuleDirectory(moduleBaseDirectory, moduleId);

        _context.Modules.Remove(module);
        _context.ModuleEndpoints.RemoveRange(module.SubEndpoints);
        _context.ModuleVersions.RemoveRange(module.Versions);
        await _context.SaveChangesAsync(ct);

        _reverseProxyService.RemoveModule(moduleId);
        _logger.LogInformation("Deleted module with ID '{ModuleId}', its versions, and its associated sub-endpoints.", moduleId);
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<ModuleDto>> GetAllModulesAsync(ModuleQueryDto? moduleQuery = null, PaginationQueryDto? pagination = null, CancellationToken ct = default)
    {
        pagination ??= new PaginationQueryDto();
        var page = pagination.Page < 1 ? PaginationQueryDto.DefaultPage : pagination.Page;
        var pageSize = pagination.PageSize < 1
            ? PaginationQueryDto.DefaultPageSize
            : Math.Min(pagination.PageSize, PaginationQueryDto.MaxPageSize);

        IQueryable<Module> query = _context.Modules.AsNoTracking();
        if (moduleQuery is not null)
        {
            if (moduleQuery.Status.HasValue)
                query = query.Where(m => m.Status == moduleQuery.Status.Value);
        }

        var totalCount = await query.CountAsync(ct);
        var modules = await query
            .OrderByDescending(m => m.ModuleName)
            .ThenBy(m => m.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        await ReconcileRunningModulesAsync(modules, ct);
        var moduleDtos = modules.Select(module => module.ToModuleDto()).ToList();

        _logger.LogDebug("Retrieved {Count} modules from the database.", moduleDtos.Count);

        return new PagedResultDto<ModuleDto>
        {
            Items = moduleDtos,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    /// <inheritdoc/>
    public async Task<ModuleDetailDto> GetModuleByIdAsync(Guid moduleId, CancellationToken ct = default)
    {
        var module = await _context.Modules
            .Include(m => m.SubEndpoints)
                .ThenInclude(e => e.ModuleVersion)
            .Include(m => m.Versions)
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == moduleId, ct);
        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        await ReconcileRunningModulesAsync([module], ct);
        return module.ToModuleDetailDto();
    }

    /// <inheritdoc/>
    public async Task<ModuleStatsDto> GetModuleStatsAsync(Guid moduleId, CancellationToken ct = default)
    {
        var module = await _context.Modules
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == moduleId, ct);
        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        if (string.IsNullOrWhiteSpace(module.ContainerId))
            return new ModuleStatsDto();

        var stats = await _dockerService.GetModuleStatsAsync(module.ContainerId, ct);
        return stats.ToModuleStatsDto();
    }

    /// <inheritdoc/>
    public async Task UpdateModuleAsync(Guid moduleId, UpdateModuleDto dto, CancellationToken ct = default)
    {
        var module = await _context.Modules
            .Include(m => m.SubEndpoints)
            .SingleOrDefaultAsync(m => m.Id == moduleId, ct);
        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        module.UpdateModuleDto(dto);
        
        await _context.SaveChangesAsync(ct);
        _moduleEndpointExtension.SynchronizeReverseProxy(module);
        _logger.LogInformation("Updated module with ID '{ModuleId}' in the database.", moduleId);
    }

    private async Task ReconcileRunningModulesAsync(IReadOnlyCollection<Module> modules, CancellationToken ct)
    {
        var crashedModules = new List<Module>();

        foreach (var module in modules.Where(m => m.Status == ModuleStatus.Running))
        {
            var isRunning = !string.IsNullOrWhiteSpace(module.ContainerId)
                && await _dockerService.IsModuleRunningAsync(module.ContainerId, ct);

            if (!isRunning)
            {
                _logger.LogWarning(
                    "Container '{ContainerId}' for running module '{ModuleId}' is no longer active. Marking module as Failed.",
                    module.ContainerId,
                    module.Id);
                module.Status = ModuleStatus.Failed;
                ModuleEndpointExtension.DeactivateActiveSubEndpoints(module);
                _reverseProxyService.RemoveModule(module.Id);
                crashedModules.Add(module);
            }
        }

        if (crashedModules.Count == 0)
            return;

        foreach (var crashed in crashedModules)
        {
            var tracked = _context.ChangeTracker.Entries<Module>()
                .FirstOrDefault(e => e.Entity.Id == crashed.Id)?.Entity;

            if (tracked is not null)
            {
                await _context.Entry(tracked).Collection(m => m.SubEndpoints).LoadAsync(ct);
                tracked.Status = ModuleStatus.Failed;
                ModuleEndpointExtension.DeactivateActiveSubEndpoints(tracked);
            }
            else
            {
                var dbModule = await _context.Modules
                    .Include(m => m.SubEndpoints)
                    .SingleOrDefaultAsync(m => m.Id == crashed.Id, ct);
                if (dbModule is not null)
                {
                    dbModule.Status = ModuleStatus.Failed;
                    ModuleEndpointExtension.DeactivateActiveSubEndpoints(dbModule);
                }
            }
        }

        await _context.SaveChangesAsync(ct);
    }
}