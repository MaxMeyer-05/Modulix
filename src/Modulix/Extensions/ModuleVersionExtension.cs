using Microsoft.EntityFrameworkCore;
using Modulix.Database.DbContexts;

namespace Modulix.Extensions;

/// <summary>
/// 
/// </summary>
public class ModuleVersionExtension
{
    /// <summary>
    /// The module settings to be used by this extension.
    /// </summary>
    private readonly ModuleSettings _moduleSettings;

    /// <summary>
    /// The server context to be used by this extension.
    /// </summary>
    private readonly ServerContext _context;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<ModuleVersionExtension> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleVersionExtension"/> class.
    /// </summary>
    /// <param name="moduleSettings">The module settings to be used by this extension.</param>
    /// <param name="context">The server context to be used by this extension.</param>
    /// <param name="logger">The logger.</param>
    public ModuleVersionExtension(
        ModuleSettings moduleSettings, 
        ServerContext context, 
        ILogger<ModuleVersionExtension> logger)
    {
        _moduleSettings = moduleSettings;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Determines whether the current version count has reached the allowed version limit.
    /// </summary>
    /// <param name="currentVersionCount">The current version count of the module.</param>
    /// <returns>True if the allowed version limit has been reached; otherwise, false.</returns>
    public bool HasReachedAllowedVersionLimit(int currentVersionCount)
    {
        if (_moduleSettings.MaxRetainedVersionsPerModule == 0)
            return false;
        
        return currentVersionCount >= _moduleSettings.MaxRetainedVersionsPerModule;
    }

    /// <summary>
    /// Gets the storage path for a specific module version.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="version">The version of the module.</param>
    /// <returns>The storage path of the module version.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the module version is not found.</exception>
    public async Task<string> GetModuleVersionDirectory(Guid moduleId, string version)
    {
        var moduleVersion = await _context.ModuleVersions
            .Where(mv => mv.ModuleId == moduleId && mv.Version == version)
            .SingleOrDefaultAsync();

        if (moduleVersion == null)
            throw new InvalidOperationException($"Module directory for module ID '{moduleId}' and version '{version}' not found.");

        return moduleVersion.StoragePath;
    }
}