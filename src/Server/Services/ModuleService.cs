using System.IO.Compression;
using Microsoft.EntityFrameworkCore;

using Server.Mappers;

using Server.Models.Dtos;
using Server.Models.Enums;

using Server.Database.Entities;
using Server.Database.DbContexts;

using Server.Services.Interfaces;

namespace Server.Services;

/// <inheritdoc cref="IModuleService"/>
public class ModuleService : IModuleService
{
    private const int MaximumArchiveEntryCount = 1_000; // 1,000 entries maximum
    private const long MaximumArchiveUncompressedBytes = 512L * 1024 * 1024; // 512 MB

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
    /// The base path for storing module-related files.
    /// </summary>
    private readonly string _storageBasePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleService"/> class.
    /// </summary>
    /// <param name="context">The database context used by the service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="env">The host environment.</param>
    /// <param name="dockerService">The Docker service.</param>
    /// <param name="endpointScanner">The module endpoint scanner.</param>
    public ModuleService(
        ServerContext context, 
        ILogger<ModuleService> logger,
        IHostEnvironment env,
        IDockerService dockerService,
        IModuleEndpointScanner endpointScanner)
    {
        _context = context;
        _logger = logger;
        _endpointScanner = endpointScanner;
        _dockerService = dockerService;
        _storageBasePath = Path.Combine(env.ContentRootPath, "storage", "modules");
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
            foreach (var endpoint in pendingEndpoints)
            {
                endpoint.Status = ModuleEndpointsStatus.PendingConfirmation;
                _context.Entry(endpoint).State = EntityState.Unchanged;
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
        var availablePort = GetAvailablePort(usedPorts, dto.ContainerPort ?? 0);

        // Ensure the used base endpoint paths are not conflicting
        var usedBasePaths = await _context.Modules.Select(m => m.BaseEndpointPath).ToListAsync(ct);
        CheckBaseEndpointPathConflict(usedBasePaths, normalizedBasePath);

        // Create the module entity and set the available container port
        var module = dto.ToModuleEntity();
        module.BaseEndpointPath = normalizedBasePath;
        module.ContainerPort = availablePort;

        // Prepare the target path for extracting the module file
        var targetPath = await CreateModuleTargetPathAsync(module.Id, dto.ModuleFile, ct);

        // Set the storage path for the module
        module.StoragePath = targetPath;

        ModuleScanResultDto result;
        try
        {
            result = await _endpointScanner.ScanDirectoryAsync(module.StoragePath, ct);
            module.ModuleEntryAssemblyFileName = result.EntryAssemblyFileName;
        }
        catch
        {
            DeleteModuleDirectory(module.StoragePath, module.Id);
            throw;
        }

        var discoveredEndpoints = result.DiscoveredEndpoints;

        // Create a discrepancy report
        var discrepancyReport = CreateEndpointDiscrepancyReport(dto.InitialEndpoints?.ToList(), discoveredEndpoints, module.Id);
    
        if (discrepancyReport?.HasDiscrepancy == true)
        {
            module.Status = ModuleStatus.PendingConfirmation;
        }
        else
        {
            try
            {
                module.Status = ModuleStatus.Starting;

                var containerId = await _dockerService.BuildContainerAsync(module.Id, module.StoragePath, result.EntryAssemblyFileName, module.ContainerPort, ct);
                module.ContainerId = containerId;

                await _dockerService.RunContainerAsync(containerId, ct);
                module.Status = ModuleStatus.Running;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to provision Docker container for module {ModuleId}.", module.Id);
                module.Status = ModuleStatus.Failed;
            }
        }

        _context.Modules.Add(module);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch
        {
            if (string.IsNullOrWhiteSpace(module.ContainerId) ||
                await TryRemoveModuleContainerAsync(module.ContainerId, module.Id))
            {
                DeleteModuleDirectory(module.StoragePath, module.Id);
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
            DiscrepancyReport = discrepancyReport
        };
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
    public async Task<IEnumerable<ModuleEndpointDto>> GetModuleSubEndpointsAsync(Guid moduleId, CancellationToken ct = default)
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
    public async Task UpdateModuleFilesAsync(Guid moduleId, UpdateModuleFilesDto file, CancellationToken ct = default)
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
        var versionDirectory = Path.Combine(_storageBasePath, $"{module.Id:N}.{Guid.NewGuid():N}");
        var tmpZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        string? candidateId = null;

        _logger.LogDebug("Temporary ZIP path for module with ID '{ModuleId}': {TmpZipPath}", moduleId, tmpZipPath);
        
        try
        {
            await using (var stream = new FileStream(tmpZipPath, FileMode.CreateNew))
            {
                await file.ModuleFile.CopyToAsync(stream, ct);
            }

            ValidateModuleArchive(tmpZipPath);
            Directory.CreateDirectory(versionDirectory);
            ZipFile.ExtractToDirectory(tmpZipPath, versionDirectory);
            ct.ThrowIfCancellationRequested();

            var scanResult = await _endpointScanner.ScanDirectoryAsync(versionDirectory, ct);
            var registeredEndpoints = module.SubEndpoints
                .Select(endpoint => (endpoint.HttpMethod.ToUpperInvariant(), endpoint.EndpointPath))
                .ToHashSet();
            var discoveredEndpoints = scanResult.DiscoveredEndpoints
                .Select(endpoint => (endpoint.HttpMethod.ToUpperInvariant(), endpoint.EndpointPath))
                .ToHashSet();

            if (!registeredEndpoints.SetEquals(discoveredEndpoints))
                throw new InvalidOperationException("The updated module changes its endpoint contract. The active version has been preserved.");

            candidateId = await _dockerService.BuildContainerAsync(
                module.Id, versionDirectory, scanResult.EntryAssemblyFileName, module.ContainerPort, ct);
            await _dockerService.RunContainerAsync(candidateId, ct);
            await _dockerService.WaitUntilReadyAsync(candidateId, ct);

            module.ContainerId = candidateId;
            module.StoragePath = versionDirectory;
            module.ModuleEntryAssemblyFileName = scanResult.EntryAssemblyFileName;
            module.Status = ModuleStatus.Running;

            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            (module.ContainerId, module.StoragePath, module.ModuleEntryAssemblyFileName, module.Status) = previousVersion;
            if (candidateId is null || await TryRemoveModuleContainerAsync(candidateId, module.Id))
                DeleteModuleDirectory(versionDirectory, module.Id);

            _logger.LogError(ex, "An error occurred while updating module files for module with ID '{ModuleId}'.", moduleId);
            throw;
        }
        finally
        {
            DeleteTemporaryFile(tmpZipPath, moduleId);
        }

        if (string.IsNullOrWhiteSpace(previousVersion.ContainerId) ||
            await TryRemoveModuleContainerAsync(previousVersion.ContainerId, module.Id))
        {
            DeleteModuleDirectory(previousVersion.StoragePath, module.Id);
        }

        _logger.LogInformation("Activated replacement container '{ContainerId}' for module '{ModuleId}'.", candidateId, moduleId);
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

    /// <summary>
    /// Validates the contents of a module archive.
    /// </summary>
    /// <param name="archivePath">The path to the module archive to validate.</param>
    private static void ValidateModuleArchive(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        if (archive.Entries.Count > MaximumArchiveEntryCount)
            throw new InvalidOperationException("Module archive exceeds the maximum allowed number of entries.");

        var totalUncompressedBytes = 0L;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaximumArchiveUncompressedBytes - totalUncompressedBytes)
                throw new InvalidOperationException("Module archive exceeds the maximum allowed uncompressed size.");

            totalUncompressedBytes += entry.Length;
        }
    }

    /// <summary>
    /// Deletes a temporary file used during module update.
    /// </summary>
    /// <param name="filePath">The path to the temporary file to delete.</param>
    /// <param name="moduleId">The ID of the module associated with the temporary file.</param>
    private void DeleteTemporaryFile(string filePath, Guid moduleId)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temporary ZIP file for module with ID '{ModuleId}'.", moduleId);
        }
    }

    /// <summary>
    /// Deletes a directory used to store module files.
    /// </summary>
    /// <param name="directoryPath">The path to the staging directory to delete.</param>
    /// <param name="moduleId">The ID of the module associated with the staging directory.</param>
    private void DeleteModuleDirectory(string directoryPath, Guid moduleId)
    {
        try
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete module directory for module with ID '{ModuleId}'.", moduleId);
        }
    }

    /// <summary>
    /// Gets an available container port, 
    /// either the selected port if it is not in use, 
    /// or the first available port in the range 1024-49151.
    /// </summary>
    /// <param name="usedPorts">The list of currently used ports.</param>
    /// <param name="selectedPort">The desired port, or 0 to automatically select an available port.</param>
    /// <returns>The available port.</returns>
    private static int GetAvailablePort(IEnumerable<int> usedPorts, int selectedPort)
    {
        int availablePort;
        if (selectedPort > 0)
        {
            if (usedPorts.Contains(selectedPort))
                throw new ArgumentException($"Container port '{selectedPort}' is already in use.");

            availablePort = selectedPort;
        }
        else
        {            
            availablePort = Enumerable.Range(1024, 48127).Except(usedPorts).FirstOrDefault();
        }

        return availablePort;
    }

    /// <summary>
    /// Checks for conflicts with existing base endpoint paths.
    /// Throws an ArgumentException if a conflict is found.
    /// </summary>
    /// <param name="usedBasePaths">The list of currently used base endpoint paths.</param>
    /// <param name="normalizedBasePath">The normalized base endpoint path to check.</param>
    /// <exception cref="ArgumentException">Thrown if a conflict is found with existing base endpoint paths.</exception>
    private static void CheckBaseEndpointPathConflict(IEnumerable<string> usedBasePaths, string normalizedBasePath)
    {
        if (usedBasePaths.Contains(normalizedBasePath))
            throw new ArgumentException($"Base endpoint path '{normalizedBasePath}' is already in use.");
        else if (usedBasePaths.Any(ubp => ubp.StartsWith(normalizedBasePath)))
            throw new ArgumentException($"Base endpoint path '{normalizedBasePath}' conflicts with an existing base endpoint path.");
        else if (usedBasePaths.Any(ubp => normalizedBasePath.StartsWith(ubp)))
            throw new ArgumentException($"Base endpoint path '{normalizedBasePath}' is a sub-path of an existing base endpoint path.");
    }

    /// <summary>
    /// Creates the target path for a module by extracting its files from the provided .zip archive.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="file">The .zip archive containing the module files.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The path to the extracted module files.</returns>
    private async Task<string> CreateModuleTargetPathAsync(Guid moduleId, IFormFile file, CancellationToken ct = default)
    {
        var targetPath = Path.Combine(_storageBasePath, moduleId.ToString());
        if (file.Length == 0)
            throw new ArgumentException("Module file cannot be empty.");
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Module file must be a .zip archive.");
            
        if (Directory.Exists(targetPath))
            Directory.Delete(targetPath, true);
        
        try
        {
            Directory.CreateDirectory(targetPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create target directory '{TargetPath}'.", targetPath);
            throw;
        }

        var tmpZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        try
        {
            using (var stream = new FileStream(tmpZipPath, FileMode.CreateNew))
            {
                await file.CopyToAsync(stream, ct);
            }

            ValidateModuleArchive(tmpZipPath);
            ZipFile.ExtractToDirectory(tmpZipPath, targetPath, true);
            _logger.LogInformation("Module files extracted to '{TargetPath}'.", targetPath);
        }
        catch
        {
            DeleteModuleDirectory(targetPath, moduleId);
            throw;
        }
        finally
        {
            DeleteTemporaryFile(tmpZipPath, moduleId);
        }

        return targetPath;
    }

    /// <summary>
    /// Creates a report detailing discrepancies between the initial and discovered endpoints for a module.
    /// </summary>
    /// <param name="initialEndpoints">The list of initially defined endpoints for the module.</param>
    /// <param name="discoveredEndpoints">The list of endpoints discovered for the module.</param>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <returns>A report detailing any discrepancies between the initial and discovered endpoints.</returns>
    private EndpointDiscrepancyReportDto? CreateEndpointDiscrepancyReport(
        List<CreateModuleEndpointDto>? initialEndpoints, 
        List<DiscoveredEndpointDto> discoveredEndpoints, 
        Guid moduleId)
    {
        EndpointDiscrepancyReportDto discrepancyReport = new();
        if (initialEndpoints is null || initialEndpoints.Count == 0)
        {
            foreach (var endpoint in discoveredEndpoints)
            {
                _context.ModuleEndpoints.Add(new ModuleEndpoint
                {
                    ModuleId = moduleId,
                    HttpMethod = endpoint.HttpMethod,
                    EndpointPath = endpoint.EndpointPath
                });
            }
        }
        else
        {
            foreach (var endpoint in initialEndpoints)
            {
                var discoveredEndpoint = discoveredEndpoints.Find(de =>
                    de.HttpMethod == endpoint.HttpMethod &&
                    de.EndpointPath == endpoint.EndpointPath);

                if (discoveredEndpoint is null)
                {
                    _context.ModuleEndpoints.Add(new ModuleEndpoint
                    {
                        ModuleId = moduleId,
                        HttpMethod = endpoint.HttpMethod,
                        EndpointPath = endpoint.EndpointPath,
                        Status = ModuleEndpointsStatus.PendingConfirmation
                    });

                    discrepancyReport.MissingEndpoints ??= [];
                    discrepancyReport.MissingEndpoints.Add(new DiscoveredEndpointDto { HttpMethod = endpoint.HttpMethod, EndpointPath = endpoint.EndpointPath });
                    discrepancyReport.HasDiscrepancy = true;
                }
                else
                {
                    _context.ModuleEndpoints.Add(new ModuleEndpoint
                    {
                        ModuleId = moduleId,
                        HttpMethod = endpoint.HttpMethod,
                        EndpointPath = endpoint.EndpointPath
                    });

                    discrepancyReport.MatchedEndpoints ??= [];
                    discrepancyReport.MatchedEndpoints.Add(new DiscoveredEndpointDto { HttpMethod = endpoint.HttpMethod, EndpointPath = endpoint.EndpointPath });
                }
            }

            foreach (var endpoint in discoveredEndpoints.Where(discoveredEndpoint =>
                         !initialEndpoints.Any(initialEndpoint =>
                             initialEndpoint.HttpMethod == discoveredEndpoint.HttpMethod &&
                             initialEndpoint.EndpointPath == discoveredEndpoint.EndpointPath)))
            {
                _context.ModuleEndpoints.Add(new ModuleEndpoint
                {
                    ModuleId = moduleId,
                    HttpMethod = endpoint.HttpMethod,
                    EndpointPath = endpoint.EndpointPath,
                    Status = ModuleEndpointsStatus.PendingConfirmation
                });

                discrepancyReport.ExtraEndpoints ??= [];
                discrepancyReport.ExtraEndpoints.Add(endpoint);
                discrepancyReport.HasDiscrepancy = true;
            }
        }
        
        return discrepancyReport ?? null;
    }
}