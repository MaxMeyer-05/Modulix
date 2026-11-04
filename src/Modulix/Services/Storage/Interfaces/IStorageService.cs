namespace Modulix.Services.Storage.Interfaces;

/// <summary>
/// Provides common operations for files and directories inside configured storage.
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Creates a storage-relative directory and optionally grants container write access.
    /// </summary>
    string EnsureDirectory(string relativePath, bool writableForContainers = false);

    /// <summary>
    /// Creates or replaces a storage directory and runs the supplied operation inside it.
    /// The directory is removed if the operation fails.
    /// </summary>
    Task<string> CreateDirectoryAsync(
        string relativePath,
        Func<string, CancellationToken, Task> populate,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a storage-relative or storage-contained absolute path.
    /// </summary>
    string ResolvePath(string path);

    /// <summary>
    /// Saves an uploaded file to a storage-relative path and returns that relative path.
    /// </summary>
    Task<string> SaveFileAsync(IFormFile content, string relativeFilePath, CancellationToken ct = default);

    /// <summary>
    /// Deletes a storage directory and any empty parent directories below the storage root.
    /// </summary>
    void DeleteDirectory(string path, string? resourceId = null);
}
