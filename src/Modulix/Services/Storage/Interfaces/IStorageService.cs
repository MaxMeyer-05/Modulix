namespace Modulix.Services.Storage.Interfaces;

/// <summary>
/// Provides common operations for files and directories inside configured storage.
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Creates a storage-relative directory and optionally grants container write access.
    /// </summary>
    /// <param name="relativePath">The storage-relative path of the directory to create.</param>
    /// <param name="writableForContainers">Whether to grant write access to container processes.</param>
    /// <returns>The storage path of the ensured directory.</returns>
    string EnsureDirectory(string relativePath, bool writableForContainers = false);

    /// <summary>
    /// Creates or replaces a storage directory and runs the supplied operation inside it.
    /// The directory is removed if the operation fails.
    /// </summary>
    /// <param name="relativePath">The storage-relative path of the directory to create.</param>
    /// <param name="populate">The operation to run inside the created directory.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The storage path of the created directory.</returns>
    /// <exception cref="ArgumentException">Thrown if the relative path is invalid.</exception>
    Task<string> CreateDirectoryAsync(
        string relativePath,
        Func<string, CancellationToken, Task> populate,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a storage-relative or storage-contained absolute path.
    /// </summary>
    /// <param name="path">The storage-relative or storage-contained absolute path to resolve.</param>
    /// <returns>The resolved absolute path.</returns>
    /// <exception cref="ArgumentException">Thrown if the path is invalid.</exception>
    string ResolvePath(string path);

    /// <summary>
    /// Saves an uploaded file to a storage-relative path and returns that relative path.
    /// </summary>
    /// <param name="content">The uploaded file content.</param>
    /// <param name="relativeFilePath">The storage-relative file path to save the content to.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The storage-relative path of the saved file.</returns>
    /// <exception cref="ArgumentException">Thrown if the relative file path is invalid.</exception>
    Task<string> SaveFileAsync(IFormFile content, string relativeFilePath, CancellationToken ct = default);

    /// <summary>
    /// Deletes a storage directory and any empty parent directories below the storage root.
    /// </summary>
    /// <param name="path">The storage-relative or absolute path of the directory to delete.</param>
    /// <param name="resourceId">An optional resource identifier associated with the directory.</param>
    void DeleteDirectory(string path, string? resourceId = null);
}
