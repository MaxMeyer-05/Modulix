using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Modulix.Database.DbContexts;
using Modulix.Database.Entities;

namespace Modulix.Extensions;

/// <summary>
/// Provides helper methods for enforcing module version retention limits and resolving version directories.
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
    /// <param name="options">The module settings options to be used by this extension.</param>
    /// <param name="context">The server context to be used by this extension.</param>
    /// <param name="logger">The logger.</param>
    public ModuleVersionExtension(
        IOptions<ModuleSettings> options, 
        ServerContext context, 
        ILogger<ModuleVersionExtension> logger)
    {
        _moduleSettings = options.Value;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Determines whether the current version count exceeds the configured version retention limit.
    /// </summary>
    /// <param name="currentVersionCount">The current version count of the module.</param>
    /// <returns><c>true</c> if the version count exceeds <see cref="ModuleSettings.MaxRetainedVersionsPerModule"/>; otherwise, <c>false</c>.</returns>
    public bool HasReachedAllowedVersionLimit(int currentVersionCount)
    {
        if (_moduleSettings.MaxRetainedVersionsPerModule <= 0)
            return false;
        
        return currentVersionCount > _moduleSettings.MaxRetainedVersionsPerModule;
    }

    /// <summary>
    /// Returns the oldest inactive versions of the module that exceed <see cref="ModuleSettings.MaxRetainedVersionsPerModule"/>.
    /// </summary>
    /// <param name="module">The module entity containing all versions.</param>
    /// <returns>The list of oldest inactive versions to remove, ordered from oldest to newest.</returns>
    public IReadOnlyList<ModuleVersion> GetExcessInactiveVersions(Module module)
    {
        if (!HasReachedAllowedVersionLimit(module.Versions.Count))
            return [];

        var excessCount = module.Versions.Count - _moduleSettings.MaxRetainedVersionsPerModule;
        return module.Versions
            .Where(v => !string.Equals(v.Version, module.CurrentVersion, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.ReleasedAtUtc)
            .ThenBy(v => v.Id)
            .Take(excessCount)
            .ToList();
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