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
        
        await _context.SaveChangesAsync(ct);
        return module.ToModuleDetailDto();
    }

    /// <inheritdoc/>
    public async Task<ModuleCreationResultDto> CreateModuleAsync(CreateModuleDto dto, CancellationToken ct = default)
    {
        throw new NotImplementedException();
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

        if (Directory.Exists(module.StoragePath))
            Directory.Delete(module.StoragePath, true);

        if (!file.ModuleFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The module file must be a ZIP archive.");

        Directory.CreateDirectory(module.StoragePath);

        var tmpZipPath = Path.Combine(Path.GetTempPath(), file.ModuleFile.FileName);
        try
        {
            await using (var stream = new FileStream(tmpZipPath, FileMode.Create))
            {
                await file.ModuleFile.CopyToAsync(stream, ct);
            }

            ZipFile.ExtractToDirectory(tmpZipPath, module.StoragePath, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while updating module files for module with ID '{ModuleId}'.", moduleId);
        }
        finally
        {
            if (File.Exists(tmpZipPath))
                File.Delete(tmpZipPath);
        }

        // TODO: Update the associated Docker container and check for sub-endpoints.
    }
}