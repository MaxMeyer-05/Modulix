namespace Modulix.Services.Storage.Interfaces;

/// <summary>
/// Provides module-specific storage operations.
/// </summary>
public interface IModuleStorageService
{
    /// <summary>
    /// Creates a module version directory by validating and extracting its uploaded ZIP archive.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="version">The version of the module.</param>
    /// <param name="file">The uploaded ZIP archive containing the module version.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The storage path of the created module version directory.</returns>
    /// <exception cref="ArgumentException">Thrown if the uploaded file is not a valid ZIP archive.</exception>
    Task<string> CreateModuleTargetPathAsync(Guid moduleId, string version, IFormFile file, CancellationToken ct = default);

    /// <summary>
    /// Gets the persistent module data directory path.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <returns>The path of the module data directory.</returns>
    string GetModuleDataDirectoryPath(Guid moduleId);

    /// <summary>
    /// Creates the persistent module data directory and grants container write access.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <returns>The path of the ensured module data directory.</returns>
    string EnsureModuleDataDirectory(Guid moduleId);

    /// <summary>
    /// Deletes a module's persistent data directory.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    void DeleteModuleDataDirectory(Guid moduleId);

    /// <summary>
    /// Deletes a module storage directory.
    /// </summary>
    /// <param name="directoryPath">The path of the directory to delete.</param>
    /// <param name="moduleId">The ID of the module, if applicable.</param>
    void DeleteDirectory(string directoryPath, Guid? moduleId = null);
}
