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
}