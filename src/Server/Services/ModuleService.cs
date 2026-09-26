using System.IO.Compression;
using Microsoft.EntityFrameworkCore;

using Server.Mappers;

using Server.Models.Dtos;
using Server.Models.Enums;

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
    /// The base path for storing module-related files.
    /// </summary>
    private readonly string _storageBasePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleService"/> class.
    /// </summary>
    /// <param name="context">The database context used by the service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="env">The host environment.</param>
    public ModuleService(
        ServerContext context, 
        ILogger<ModuleService> logger,
        IHostEnvironment env)
    {
        _context = context;
        _logger = logger;
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
            if (dto.ConfirmedEndpoints.Any(ce => ce.MissingEndpoints?.Any(me => me.Id == endpoint.Id) == true))
            {
                endpoint.Status = ModuleEndpointsStatus.Active;
            }
            else if (dto.ConfirmedEndpoints.Any(ce => ce.ExtraEndpoints?.Any(ee => ee.Id == endpoint.Id) == true))
            {
                _context.ModuleEndpoints.Remove(endpoint);
            }
        }

        module.Status = ModuleStatus.Created;
        
        await _context.SaveChangesAsync(ct);
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
        int availablePort;
        if (dto.ContainerPort.HasValue)
        {
            if (usedPorts.Contains(dto.ContainerPort.Value))
                throw new ArgumentException($"Container port '{dto.ContainerPort.Value}' is already in use.");

            availablePort = dto.ContainerPort.Value;
        }
        else
        {            
            availablePort = Enumerable.Range(1024, 48127).Except(usedPorts).FirstOrDefault();
        }

        // Ensure the used base endpoint paths are not conflicting
        var usedBasePaths = await _context.Modules.Select(m => m.BaseEndpointPath).ToListAsync(ct);
        if (usedBasePaths.Contains(normalizedBasePath))
            throw new ArgumentException($"Base endpoint path '{normalizedBasePath}' is already in use.");
        else if (usedBasePaths.Any(ubp => ubp.StartsWith(normalizedBasePath)))
            throw new ArgumentException($"Base endpoint path '{normalizedBasePath}' conflicts with an existing base endpoint path.");
        else if (usedBasePaths.Any(ubp => normalizedBasePath.StartsWith(ubp)))
            throw new ArgumentException($"Base endpoint path '{normalizedBasePath}' is a sub-path of an existing base endpoint path.");

        // Create the module entity and set the available container port
        var module = dto.ToModuleEntity();
        module.BaseEndpointPath = normalizedBasePath;
        module.ContainerPort = availablePort;

        // Prepare the target path for extracting the module file
        var targetPath = Path.Combine(_storageBasePath, module.Id.ToString());
        var file = dto.ModuleFile;
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

        var tmpZipPath = Path.Combine(Path.GetTempPath(), file.FileName);
        try
        {
            using (var stream = new FileStream(tmpZipPath, FileMode.Create))
            {
                await file.CopyToAsync(stream, ct);
            }

            ValidateModuleArchive(tmpZipPath);
            ZipFile.ExtractToDirectory(tmpZipPath, targetPath, true);
            _logger.LogInformation("Module files extracted to '{TargetPath}'.", targetPath);
        }
        finally
        {
            DeleteTemporaryFile(tmpZipPath, module.Id);
        }

        // Set the storage path for the module
        module.StoragePath = targetPath;

        // TODO: Scan the extracted module files for endpoint definitions and other relevant metadata.
        // And check the enpoints if the user has provided any in the request.

        _context.Modules.Add(module);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Created module with ID '{ModuleId}' and stored its files at '{StoragePath}'.", module.Id, module.StoragePath);

        return new ModuleCreationResultDto
        {
            Module = module.ToModuleDetailDto(),
            DiscrepancyReport = null // TODO: Generate a discrepancy report after scanning the module files.
        };
    }

    /// <inheritdoc/>
    public async Task DeleteModuleAsync(Guid moduleId, CancellationToken ct = default)
    {
        var module = await _context.Modules.FindAsync([moduleId], ct);
        if (module is null)
            return;

        // TODO: Deletion of the Docker container or other runtime resources associated with the module.

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
        var module = await _context.Modules.FindAsync([moduleId], ct);
        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        if (!file.ModuleFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The module file must be a ZIP archive.");

        var storageDirectory = Path.GetFullPath(module.StoragePath);
        var storageParentDirectory = Path.GetDirectoryName(storageDirectory)!;
        var stagingDirectory = Path.Combine(storageParentDirectory, $".{module.Id:N}.{Guid.NewGuid():N}.staging");
        var tmpZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");

        _logger.LogDebug("Temporary ZIP path for module with ID '{ModuleId}': {TmpZipPath}", moduleId, tmpZipPath);
        
        try
        {
            await using (var stream = new FileStream(tmpZipPath, FileMode.Create))
            {
                await file.ModuleFile.CopyToAsync(stream, ct);
            }

            ValidateModuleArchive(tmpZipPath);
            Directory.CreateDirectory(stagingDirectory);
            ZipFile.ExtractToDirectory(tmpZipPath, stagingDirectory);

            if (Directory.Exists(storageDirectory))
                Directory.Delete(storageDirectory, true);

            Directory.Move(stagingDirectory, storageDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while updating module files for module with ID '{ModuleId}'.", moduleId);
            throw;
        }
        finally
        {
            DeleteTemporaryFile(tmpZipPath, moduleId);
            DeleteStagingDirectory(stagingDirectory, moduleId);
        }

        // TODO: Update the associated Docker container and check for sub-endpoints.
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
    /// Deletes a staging directory used during module update.
    /// </summary>
    /// <param name="directoryPath">The path to the staging directory to delete.</param>
    /// <param name="moduleId">The ID of the module associated with the staging directory.</param>
    private void DeleteStagingDirectory(string directoryPath, Guid moduleId)
    {
        try
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete staging directory for module with ID '{ModuleId}'.", moduleId);
        }
    }
}