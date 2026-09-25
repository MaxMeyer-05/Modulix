using Microsoft.EntityFrameworkCore;

using Server.Models.Dtos;
using Server.Models.Enums;

using Server.Mappers;
using Server.Database.DbContexts;

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
    /// Initializes a new instance of the <see cref="ModuleService"/> class.
    /// </summary>
    /// <param name="context">The database context used by the service.</param>
    /// <param name="logger">The logger.</param>
    public ModuleService(
        ServerContext context, 
        ILogger<ModuleService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ModuleDetailDto> ConfirmEndpointsAsync(Guid moduleId, ConfirmEndpointsDto dto, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public async Task<ModuleCreationResultDto> CreateModuleAsync(CreateModuleDto dto, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public async Task DeleteModuleAsync(Guid moduleId, CancellationToken ct = default)
    {
        throw new NotImplementedException();
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
        var module = await _context.Modules.FindAsync([moduleId], ct);
        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' not found.");
        
        return module.ToModuleDetailDto();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ModuleEndpointDto>> GetModuleSubEndpointsAsync(Guid moduleId, CancellationToken ct = default)
    {
        var module = await _context.Modules.FindAsync([moduleId], ct);
        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' not found.");

        var subEndpoints = module.SubEndpoints.Select(se => se.ToEndpointDto()).ToList();

        _logger.LogDebug("Retrieved {Count} sub-endpoints for module with ID '{ModuleId}'.", subEndpoints.Count, moduleId);

        return subEndpoints;
    }

    /// <inheritdoc/>
    public async Task<ModuleDto> UpdateModuleAsync(Guid moduleId, UpdateModuleDto dto, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public async Task UpdateModuleFilesAsync(Guid moduleId, IFormFile file, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public async Task UpdateModuleStatusAsync(Guid moduleId, UpdateModuleStatusDto dto, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}