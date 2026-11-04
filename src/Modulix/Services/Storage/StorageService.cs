using Microsoft.Extensions.Options;

using Modulix.Settings;
using Modulix.Services.Storage.Interfaces;

namespace Modulix.Services.Storage;

/// <inheritdoc cref="IStorageService"/>
public sealed class StorageService : IStorageService
{
    /// <summary>
    /// The base path of the storage directory.
    /// </summary>
    private readonly string _storageBasePath;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<StorageService> _logger;

    /// <summary>
    /// Initializes a storage service using the configured storage root.
    /// </summary>
    /// <param name="options">The shared storage settings containing the storage base path.</param>
    /// <param name="logger">The logger.</param>
    public StorageService(IOptions<StorageSettings> options, ILogger<StorageService> logger)
    {
        _storageBasePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Value.StorageBasePath));
        _logger = logger;
    }

    /// <inheritdoc/>
    public string EnsureDirectory(string relativePath, bool writableForContainers = false)
    {
        var fullPath = ResolveRelativePath(relativePath);
        Directory.CreateDirectory(fullPath);

        if (writableForContainers && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                fullPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        }

        return fullPath;
    }

    /// <inheritdoc/>
    public async Task<string> CreateDirectoryAsync(
        string relativePath,
        Func<string, CancellationToken, Task> populate,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(populate);
        var fullPath = ResolveRelativePath(relativePath);

        try
        {
            if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, recursive: true);

            Directory.CreateDirectory(fullPath);
            await populate(fullPath, ct);
            return fullPath;
        }
        catch (Exception ex)
        {
            DeleteDirectory(fullPath);
            _logger.LogError(ex, "Failed to create storage directory '{TargetPath}'.", fullPath);
            throw;
        }
    }

    /// <inheritdoc/>
    public string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : ResolveRelativePath(path);

        if (!IsWithinStorage(fullPath))
            throw new ArgumentException("The path must be inside the configured storage directory.", nameof(path));

        return fullPath;
    }

    /// <inheritdoc/>
    public async Task<string> SaveFileAsync(IFormFile content, string relativeFilePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
            throw new ArgumentException("Content cannot be empty.", nameof(content));

        var targetPath = ResolveRelativePath(relativeFilePath);
        var relativeDirectory = Path.GetDirectoryName(relativeFilePath);
        var parentPath = string.IsNullOrEmpty(relativeDirectory)
            ? EnsureStorageRoot()
            : EnsureDirectory(relativeDirectory);
        var fileCreated = false;
        try
        {
            await using var target = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            fileCreated = true;
            await content.CopyToAsync(target, ct);
            return Path.GetRelativePath(_storageBasePath, targetPath).Replace(Path.DirectorySeparatorChar, '/');
        }
        catch (Exception ex)
        {
            try
            {
                if (fileCreated && File.Exists(targetPath))
                    File.Delete(targetPath);
                DeleteEmptyDirectories(parentPath);
            }
            catch (Exception cleanupException)
            {
                _logger.LogWarning(cleanupException, "Failed to clean up partial file storage at '{TargetPath}'.", targetPath);
            }

            _logger.LogError(ex, "Failed to save uploaded file to '{TargetPath}'.", targetPath);
            throw;
        }
    }

    /// <inheritdoc/>
    public void DeleteDirectory(string path, string? resourceId = null)
    {
        try
        {
            var fullPath = ResolvePath(path);
            if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, recursive: true);

            DeleteEmptyDirectories(Path.GetDirectoryName(fullPath));
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(resourceId))
                _logger.LogWarning(ex, "Failed to delete storage directory for resource '{ResourceId}'.", resourceId);
            else
                _logger.LogWarning(ex, "Failed to delete storage directory '{Path}'.", path);
        }
    }

    /// <summary>
    /// Resolve a storage-relative path to an absolute path within the storage directory.
    /// </summary>
    /// <param name="relativePath">The storage-relative path to resolve.</param>
    /// <returns>The absolute path within the storage directory.</returns>
    /// <exception cref="ArgumentException">Thrown if the path is not a storage-relative path or is outside the storage directory.</exception>
    private string ResolveRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalizedPath = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(normalizedPath))
            throw new ArgumentException("A storage-relative path is required.", nameof(relativePath));

        var fullPath = Path.GetFullPath(Path.Combine(_storageBasePath, normalizedPath));
        if (!IsWithinStorage(fullPath))
            throw new ArgumentException("The path must be inside the configured storage directory.", nameof(relativePath));

        return fullPath;
    }

    /// <summary>
    /// Ensure that the storage root directory exists.
    /// </summary>
    /// <returns>The absolute path to the storage root directory.</returns>
    private string EnsureStorageRoot()
    {
        Directory.CreateDirectory(_storageBasePath);
        return _storageBasePath;
    }

    /// <summary>
    /// Check if a given full path is within the storage directory.
    /// </summary>
    /// <param name="fullPath">The full path to check.</param>
    /// <returns>True if the path is within the storage directory, false otherwise.</returns>
    private bool IsWithinStorage(string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var storagePrefix = Path.EndsInDirectorySeparator(_storageBasePath)
            ? _storageBasePath
            : _storageBasePath + Path.DirectorySeparatorChar;
        return !string.Equals(fullPath, _storageBasePath, comparison) &&
            fullPath.StartsWith(storagePrefix, comparison);
    }

    /// <summary>
    /// Delete empty directories starting from the specified path and moving up the directory tree.
    /// </summary>
    /// <param name="directoryPath">The starting directory path.</param>
    private void DeleteEmptyDirectories(string? directoryPath)
    {
        while (!string.IsNullOrEmpty(directoryPath) && IsWithinStorage(directoryPath))
        {
            if (!Directory.Exists(directoryPath) || Directory.EnumerateFileSystemEntries(directoryPath).Any())
                break;

            Directory.Delete(directoryPath, recursive: false);
            directoryPath = Path.GetDirectoryName(directoryPath);
        }
    }
}
