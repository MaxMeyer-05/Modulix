namespace Modulix.Services.Storage.Interfaces;

/// <summary>
/// Provides module-specific storage operations.
/// </summary>
public interface IModuleStorageService
{
    /// <summary>
    /// Creates a module version directory by validating and extracting its uploaded ZIP archive.
    /// </summary>
    Task<string> CreateModuleTargetPathAsync(Guid moduleId, string version, IFormFile file, CancellationToken ct = default);

    /// <summary>
    /// Gets the persistent module data directory path.
    /// </summary>
    string GetModuleDataDirectoryPath(Guid moduleId);

    /// <summary>
    /// Creates the persistent module data directory and grants container write access.
    /// </summary>
    string EnsureModuleDataDirectory(Guid moduleId);

    /// <summary>
    /// Deletes a module's persistent data directory.
    /// </summary>
    void DeleteModuleDataDirectory(Guid moduleId);

    /// <summary>
    /// Deletes a module storage directory.
    /// </summary>
    void DeleteDirectory(string directoryPath, Guid? moduleId = null);
}
