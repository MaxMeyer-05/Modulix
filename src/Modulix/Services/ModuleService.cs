using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Modulix.Mappers;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

using Modulix.Models.Enums;
using Modulix.Models.Options;

using Modulix.Database.Entities;
using Modulix.Database.DbContexts;

using Modulix.Services.Interfaces;
using Modulix.Extensions;

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
    private readonly IDockerService _dockerService;

    /// <summary>
    /// The shared module settings.
    /// </summary>
    private readonly ModulixOptions _options;

    /// <summary>
    /// The directory extension for handling module directories and archives.
    /// </summary>
    private readonly DirectoryExtension _directoryExtension;

    /// <summary>
    /// The module endpoint extension for validating and reconciling endpoints.
    /// </summary>
    private readonly ModuleEndpointExtension _moduleEndpointExtension;

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
    public ModuleService(
        ServerContext context, 
        ILogger<ModuleService> logger,
        IOptions<ModulixOptions> options,
        IDockerService dockerService,
        IModuleEndpointScanner endpointScanner,
        DirectoryExtension directoryExtension,
        ModuleEndpointExtension moduleEndpointExtension)
    {
        _context = context;
        _logger = logger;
        _endpointScanner = endpointScanner;
        _dockerService = dockerService;
        _options = options.Value;
        _directoryExtension = directoryExtension;
        _moduleEndpointExtension = moduleEndpointExtension;
    }

    /// <inheritdoc/>
    public async Task<ModuleDetailDto> ConfirmEndpointsAsync(Guid moduleId, ConfirmEndpointsDto dto, CancellationToken ct = default)
    {
        var module = await _context.Modules
        .Include(m => m.SubEndpoints)
        .SingleOrDefaultAsync(m => m.Id == moduleId, ct);

        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        if (module.Status != ModuleStatus.PendingConfirmation)
            throw new InvalidOperationException($"Module '{moduleId}' is not in PendingConfirmation status.");

        var previousEndpoints = module.SubEndpoints
            .Select(endpoint => (Endpoint: endpoint, endpoint.Status))
            .ToList();
        var pendingEndpoints = module.SubEndpoints
            .Where(e => e.Status == ModuleEndpointsStatus.PendingConfirmation)
            .ToList();

        foreach (var endpoint in pendingEndpoints)
        {
            if (dto.ConfirmedEndpoints.Any(ce => ce.MissingEndpoints?.Any(me =>
                me.HttpMethod == endpoint.HttpMethod && me.EndpointPath == endpoint.EndpointPath) == true))
            {
                endpoint.Status = ModuleEndpointsStatus.Active;
            }
            else if (dto.ConfirmedEndpoints.Any(ce => ce.ExtraEndpoints?.Any(ee =>
                ee.HttpMethod == endpoint.HttpMethod && ee.EndpointPath == endpoint.EndpointPath) == true))
            {
                _context.ModuleEndpoints.Remove(endpoint);
            }
        }

        if (dto.ConfirmedEndpoints.Count == 0 || pendingEndpoints.Any(endpoint =>
                endpoint.Status == ModuleEndpointsStatus.PendingConfirmation &&
                _context.Entry(endpoint).State != EntityState.Deleted))
        {
            await _context.SaveChangesAsync(ct);
            return module.ToModuleDetailDto();
        }

        module.Status = ModuleStatus.Created;

        var previousContainerId = module.ContainerId;
        string? candidateId = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(previousContainerId))
            {
                await TryRemoveModuleContainerAsync(previousContainerId, module.Id);
                module.ContainerId = null;
            }

            module.Status = ModuleStatus.Starting;
            candidateId = await _dockerService.BuildContainerAsync(module.Id, module.StoragePath, module.ModuleEntryAssemblyFileName, module.ContainerPort, ct);
            module.ContainerId = candidateId;            
            await _dockerService.RunContainerAsync(candidateId, ct);
            module.Status = ModuleStatus.Running;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start confirmed module container {ModuleId}.", module.Id);
            module.Status = ModuleStatus.Failed;
        }
        
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch
        {
            if (candidateId is not null)
                await TryRemoveModuleContainerAsync(candidateId, module.Id);
            
            module.ContainerId = previousContainerId;
            module.Status = ModuleStatus.PendingConfirmation;
            _context.Entry(module).State = EntityState.Unchanged;
            foreach (var previousEndpoint in previousEndpoints)
            {
                previousEndpoint.Endpoint.Status = previousEndpoint.Status;
                _context.Entry(previousEndpoint.Endpoint).State = EntityState.Unchanged;
                if (!module.SubEndpoints.Contains(previousEndpoint.Endpoint))
                    module.SubEndpoints.Add(previousEndpoint.Endpoint);
            }
            throw;
        }
        return module.ToModuleDetailDto();
    }

    /// <inheritdoc/>
    public async Task<ModuleCreationResultDto> CreateModuleAsync(CreateModuleDto dto, CancellationToken ct = default)
    {
        // Normalize the base endpoint path
        if (string.IsNullOrWhiteSpace(dto.BaseEndpointPath))
            throw new ArgumentException("Base endpoint path cannot be empty.");
        var normalizedBasePath = dto.BaseEndpointPath.Trim().TrimEnd('/').TrimStart('/');
        
        // Ensure the used container ports are not conflicting
        var usedPorts = await _context.Modules.Select(m => m.ContainerPort).ToListAsync(ct);
        var availablePort = PortExtension.GetAvailablePort(usedPorts, dto.ContainerPort ?? 0);

        // Ensure the used base endpoint paths are not conflicting
        var usedBasePaths = await _context.Modules.Select(m => m.BaseEndpointPath).ToListAsync(ct);
        ModuleEndpointExtension.CheckBaseEndpointPathConflict(usedBasePaths, normalizedBasePath);

        // Create the module entity and set the available container port
        var module = dto.ToModuleEntity();
        module.BaseEndpointPath = normalizedBasePath;
        module.ContainerPort = availablePort;

        // Prepare the target path for extracting the module file
        var targetPath = await _directoryExtension.CreateModuleTargetPathAsync(module.Id, dto.ModuleFile, ct);

        // Set the storage path for the module
        module.StoragePath = targetPath;

        // Add initial sub-endpoints for the module
        var manualRoutes = dto.InitialEndpoints?.ToList() ?? [];
        foreach (var ep in manualRoutes)
        {
            module.SubEndpoints.Add(new ModuleEndpoint
            {
                ModuleId = module.Id,
                HttpMethod = ep.HttpMethod,
                EndpointPath = ep.EndpointPath,
                Status = ModuleEndpointsStatus.PendingConfirmation 
            });
        }

        // Scan the module for endpoints and handle any errors during the scanning process
        ScanEnqueueResponse scanResponse;
        try
        {
            scanResponse = await _endpointScanner.ScanOrEnqueueAsync(module.Id, module.StoragePath, ScanPriority.Create, ct);
            module.ModuleEntryAssemblyFileName = scanResponse.WasQueued
                ? string.Empty
                : (scanResponse.Result ?? throw new InvalidOperationException("The scanner did not return a scan result.")).EntryAssemblyFileName;
        }
        catch
        {
            _directoryExtension.DeleteModuleDirectory(module.StoragePath, module.Id);
            throw;
        }

        EndpointDiscrepancyReportDto? discrepancyReport = null;
        // Log the final status of the module after attempting to provision the Docker container
        try
        {
            _context.Modules.Add(module);
            if (scanResponse.WasQueued)
            {
                module.Status = ModuleStatus.QueuedForScan;
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
                await TryRemoveModuleContainerAsync(module.ContainerId, module.Id))
            {
                _directoryExtension.DeleteModuleDirectory(module.StoragePath, module.Id);
            }

            _context.Entry(module).State = EntityState.Detached;
            foreach (var endpoint in _context.ChangeTracker.Entries<ModuleEndpoint>()
                .Where(entry => entry.Entity.ModuleId == module.Id).ToList())
            {
                endpoint.State = EntityState.Detached;
            }

            throw;
        }

        _logger.LogInformation("Created module with ID '{ModuleId}' and stored its files at '{StoragePath}'.", module.Id, module.StoragePath);

        return new ModuleCreationResultDto
        {
            Module = module.ToModuleDetailDto(),
            DiscrepancyReport = scanResponse.WasQueued ? null : discrepancyReport ?? new EndpointDiscrepancyReportDto
            {
                ModuleId = module.Id,
                MatchedEndpoints = manualRoutes.Count == 0 ? null : manualRoutes.Select(endpoint => new DiscoveredEndpointDto
                {
                    HttpMethod = endpoint.HttpMethod,
                    EndpointPath = endpoint.EndpointPath
                }).ToList()
            },
            Message = scanResponse.WasQueued ? "Scan was queued." : "Scan completed."
        };
    }

        /// <inheritdoc/>
    public async Task ProcessQueuedScanResultAsync(Guid moduleId, ModuleScanResultDto result, CancellationToken ct = default)
    {
        var module = await _context.Modules.Include(m => m.SubEndpoints).FirstOrDefaultAsync(m => m.Id == moduleId, ct);
        if (module == null || module.Status != ModuleStatus.QueuedForScan) return;

        module.ModuleEntryAssemblyFileName = result.EntryAssemblyFileName;
        module.Status = ModuleStatus.Created;
        await _moduleEndpointExtension.CreateEndpointDiscrepancyReportAsync(module, result.DiscoveredEndpoints, ct);
    }

    /// <inheritdoc/>
    public async Task DeleteModuleAsync(Guid moduleId, CancellationToken ct = default)
    {
        var module = await _context.Modules.FindAsync([moduleId], ct);
        if (module is null)
            return;

        if (!string.IsNullOrEmpty(module.ContainerId))
        {
            await _dockerService.RemoveContainerAsync(module.ContainerId, module.Id, ct);
        }

        if (Directory.Exists(module.StoragePath))
        {
            try
            {
                Directory.Delete(module.StoragePath, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete storage path for module with ID '{ModuleId}'.", moduleId);
            }
        }

        var subEndpoints = _context.ModuleEndpoints.Where(se => se.ModuleId == moduleId).ToList();

        _context.Modules.Remove(module);
        _context.ModuleEndpoints.RemoveRange(subEndpoints);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted module with ID '{ModuleId}' and its associated sub-endpoints.", moduleId);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ModuleDto>> GetAllModulesAsync(CancellationToken ct = default)
    {
        var modules = await _context.Modules.ToListAsync(ct);
        var moduleDtos = modules.Select(module => module.ToModuleDto()).ToList();

        _logger.LogDebug("Retrieved {Count} modules from the database.", moduleDtos.Count);

        return moduleDtos;
    }

    /// <inheritdoc/>
    public async Task<ModuleDetailDto> GetModuleByIdAsync(Guid moduleId, CancellationToken ct = default)
    {
        var module = await _context.Modules
            .Include(m => m.SubEndpoints)
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == moduleId, ct);
        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        return module.ToModuleDetailDto();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ModuleEndpointDto>> GetModuleEndpointsAsync(Guid moduleId, CancellationToken ct = default)
    {
        var exists = await _context.Modules.FindAsync([moduleId], ct);
        if (exists is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        var subEndpoints = await _context.ModuleEndpoints
            .AsNoTracking()
            .Where(e => e.ModuleId == moduleId)
            .ToListAsync(ct);

        var subEndpointDtos = subEndpoints.Select(se => se.ToEndpointDto()).ToList();

        _logger.LogDebug("Retrieved {Count} sub-endpoints for module with ID '{ModuleId}'.", subEndpoints.Count, moduleId);
        return subEndpointDtos;
    }

    /// <inheritdoc/>
    public async Task UpdateModuleAsync(Guid moduleId, UpdateModuleDto dto, CancellationToken ct = default)
    {
        var module = await _context.Modules.FindAsync([moduleId], ct);
        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        module.UpdateModuleDto(dto);

        _context.ChangeTracker.DetectChanges();
        if (_context.Entry(module).State == EntityState.Unchanged)
            return;

        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Updated module with ID '{ModuleId}' in the database.", moduleId);
    }

    /// <inheritdoc/>
    public async Task<ModuleCreationResultDto> UpdateModuleFilesAsync(Guid moduleId, UpdateModuleFilesDto file, CancellationToken ct = default)
    {
        var module = await _context.Modules
            .Include(module => module.SubEndpoints)
            .SingleOrDefaultAsync(module => module.Id == moduleId, ct);

        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        if (!file.ModuleFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The module file must be a ZIP archive.");

        if (module.Status == ModuleStatus.PendingConfirmation ||
            module.SubEndpoints.Any(endpoint => endpoint.Status == ModuleEndpointsStatus.PendingConfirmation))
        {
            throw new InvalidOperationException("Module endpoints must be confirmed before updating module files.");
        }

        var previousVersion = (module.ContainerId, module.StoragePath, module.ModuleEntryAssemblyFileName, module.Status);

        var versionDirectory = Path.Combine(_options.StorageBasePath, $"{module.Id:N}.{Guid.NewGuid():N}");
        var tmpZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        string? candidateId = null;

        _logger.LogDebug("Temporary ZIP path for module with ID '{ModuleId}': {TmpZipPath}", moduleId, tmpZipPath);
        
        try
        {
            await using (var stream = new FileStream(tmpZipPath, FileMode.CreateNew))
            {
                await file.ModuleFile.CopyToAsync(stream, ct);
            }

            _directoryExtension.ValidateModuleArchive(tmpZipPath);
            Directory.CreateDirectory(versionDirectory);
            ZipFile.ExtractToDirectory(tmpZipPath, versionDirectory);
            ct.ThrowIfCancellationRequested();

            // Scan the module's directory for endpoints
            var scanResponse = await _endpointScanner.ScanOrEnqueueAsync(module.Id, versionDirectory, ScanPriority.Patch, ct);
            module.StoragePath = versionDirectory;

            if (scanResponse.WasQueued)
            {
                _logger.LogInformation("Module scan for module with ID '{ModuleId}' was queued.", moduleId);

                module.Status = ModuleStatus.QueuedForScan;
                await _context.SaveChangesAsync(ct);

                return new ModuleCreationResultDto
                {
                    Module = module.ToModuleDetailDto(),
                    Message = "Alle Scanner-Container sind besetzt. Das Update wurde in die Warteschlange aufgenommen."
                };
            }
            var report = await _moduleEndpointExtension.CreateEndpointDiscrepancyReportAsync(module, scanResponse.Result!.DiscoveredEndpoints, ct);

            module.ModuleEntryAssemblyFileName = scanResponse.Result.EntryAssemblyFileName;


            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            (module.ContainerId, module.StoragePath, module.ModuleEntryAssemblyFileName, module.Status) = previousVersion;
            _context.Entry(module).State = EntityState.Unchanged;

            if (candidateId is null || await TryRemoveModuleContainerAsync(candidateId, module.Id))
                _directoryExtension.DeleteModuleDirectory(versionDirectory, module.Id);

            _logger.LogError(ex, "An error occurred while updating module files for module with ID '{ModuleId}'.", moduleId);
            throw;
        }
        finally
        {
            _directoryExtension.DeleteTemporaryFile(tmpZipPath, moduleId);
        }

        if (!string.IsNullOrWhiteSpace(previousVersion.ContainerId) ||
            await TryRemoveModuleContainerAsync(previousVersion.ContainerId!, module.Id))
        {
            _directoryExtension.DeleteModuleDirectory(previousVersion.StoragePath, module.Id);
        }

        _logger.LogInformation("Activated replacement container '{ContainerId}' for module '{ModuleId}'.", candidateId, moduleId);
        return new ModuleCreationResultDto 
        {
            Module = module.ToModuleDetailDto(),
            Message = "Replacement container activated successfully." 
        };
    }

    /// <summary>
    /// Attempts to remove a Docker container associated with a module.
    /// </summary>
    /// <param name="containerId">The ID of the container to remove.</param>
    /// <param name="moduleId">The ID of the module associated with the container.</param>
    /// <returns>True if the container was successfully removed; otherwise, false.</returns>
    private async Task<bool> TryRemoveModuleContainerAsync(string containerId, Guid moduleId)
    {
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await _dockerService.RemoveContainerAsync(containerId, moduleId, cleanupCts.Token);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up container '{ContainerId}' for module '{ModuleId}'.", containerId, moduleId);
            return false;
        }
    }

}