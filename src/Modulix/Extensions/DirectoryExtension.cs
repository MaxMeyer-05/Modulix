using System.IO.Compression;
using Microsoft.Extensions.Options;

namespace Modulix.Extensions;

/// <summary>
/// Provides utility methods for handling module directories and archives.
/// </summary>
public class DirectoryExtension
{
    /// <summary>
    /// The shared module settings.
    /// </summary>
    private readonly ModuleSettings _options;

    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<DirectoryExtension> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryExtension"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="options">The shared module settings.</param>
    public DirectoryExtension(
        ILogger<DirectoryExtension> logger, 
        IOptions<ModuleSettings> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Validates the contents of a module archive.
    /// </summary>
    /// <param name="archivePath">The path to the module archive to validate.</param>
    public void ValidateModuleArchive(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        if (archive.Entries.Count > _options.MaximumArchiveEntryCount)
            throw new InvalidOperationException("Module archive exceeds the maximum allowed number of entries.");

        var totalUncompressedBytes = 0L;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > _options.MaximumArchiveUncompressedBytes - totalUncompressedBytes)
                throw new InvalidOperationException("Module archive exceeds the maximum allowed uncompressed size.");

            totalUncompressedBytes += entry.Length;
        }
    }

    /// <summary>
    /// Extracts the contents of a module ZIP archive to the specified destination directory,
    /// normalizing both Windows ('\') and Unix ('/') entry path separators across platforms.
    /// </summary>
    /// <param name="archivePath">The path to the module ZIP archive.</param>
    /// <param name="destinationDirectoryPath">The directory to extract the archive contents into.</param>
    /// <exception cref="IOException">Thrown if an archive entry attempts path traversal outside the destination directory.</exception>
    public void ExtractModuleArchive(string archivePath, string destinationDirectoryPath)
    {
        var fullDestinationDirPath = Path.GetFullPath(destinationDirectoryPath);
        if (!Path.EndsInDirectorySeparator(fullDestinationDirPath))
            fullDestinationDirPath += Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var normalizedEntryName = entry.FullName
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            var fileDestinationPath = Path.GetFullPath(Path.Combine(fullDestinationDirPath, normalizedEntryName));
            if (!fileDestinationPath.StartsWith(fullDestinationDirPath, comparison))
                throw new IOException("Extracting Zip entry would have resulted in a file outside the specified destination directory.");

            if (Path.GetFileName(fileDestinationPath).Length == 0)
            {
                if (entry.Length != 0)
                    throw new IOException("Zip entry name ends in directory separator character but contains data.");

                Directory.CreateDirectory(fileDestinationPath);
            }
            else
            {
                if (Path.GetDirectoryName(fileDestinationPath) is { Length: > 0 } parentDirectory)
                    Directory.CreateDirectory(parentDirectory);

                entry.ExtractToFile(fileDestinationPath, overwrite: true);
            }
        }
    }

    /// <summary>
    /// Deletes a temporary file used during module update.
    /// </summary>
    /// <param name="filePath">The path to the temporary file to delete.</param>
    /// <param name="moduleId">The ID of the module associated with the temporary file.</param>
    public void DeleteTemporaryFile(string filePath, Guid moduleId)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temporary ZIP file for module with ID '{ModuleId}'.", moduleId);
        }
    }

    /// <summary>
    /// Deletes a directory used to store module files.
    /// </summary>
    /// <param name="directoryPath">The path to the staging directory to delete.</param>
    /// <param name="moduleId">The ID of the module associated with the staging directory.</param>
    public void DeleteModuleDirectory(string directoryPath, Guid moduleId)
    {
        try
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, true);

            var storageBaseFullPath = Path.GetFullPath(_options.StorageBasePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parentDir = Path.GetDirectoryName(Path.GetFullPath(directoryPath));

            while (!string.IsNullOrEmpty(parentDir))
            {
                var normalizedParent = parentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(normalizedParent, storageBaseFullPath, StringComparison.OrdinalIgnoreCase) ||
                    !normalizedParent.StartsWith(storageBaseFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (Directory.Exists(normalizedParent) && !Directory.EnumerateFileSystemEntries(normalizedParent).Any())
                {
                    Directory.Delete(normalizedParent, false);
                    parentDir = Path.GetDirectoryName(normalizedParent);
                }
                else
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete module directory for module with ID '{ModuleId}'.", moduleId);
        }
    }

    /// <summary>
    /// Creates the target path for a module version by extracting its files from the provided .zip archive.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="version">The version string of the module.</param>
    /// <param name="file">The .zip archive containing the module files.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The path to the extracted module files.</returns>
    public async Task<string> CreateModuleTargetPathAsync(Guid moduleId, string version, IFormFile file, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Module version cannot be empty.");

        var normalizedVersion = version.Trim();
        if (normalizedVersion is "." or ".." ||
            normalizedVersion.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            normalizedVersion.Contains('/') ||
            normalizedVersion.Contains('\\'))
        {
            throw new ArgumentException("Module version contains invalid path characters.");
        }

        var targetPath = Path.Combine(_options.StorageBasePath, moduleId.ToString(), "versions", normalizedVersion);
        if (file.Length == 0)
            throw new ArgumentException("Module file cannot be empty.");
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Module file must be a .zip archive.");
            
        if (Directory.Exists(targetPath))
            Directory.Delete(targetPath, true);
        
        try
        {
            Directory.CreateDirectory(targetPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create target directory '{TargetPath}'.", targetPath);
            throw;
        }

        var tmpZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        try
        {
            using (var stream = new FileStream(tmpZipPath, FileMode.CreateNew))
            {
                await file.CopyToAsync(stream, ct);
            }

            ValidateModuleArchive(tmpZipPath);
            ExtractModuleArchive(tmpZipPath, targetPath);
            EnsureModuleDataDirectory(moduleId);
            _logger.LogInformation("Module files extracted to '{TargetPath}'.", targetPath);
        }
        catch
        {
            DeleteModuleDirectory(targetPath, moduleId);
            throw;
        }
        finally
        {
            DeleteTemporaryFile(tmpZipPath, moduleId);
        }

        return targetPath;
    }

    /// <summary>
    /// Gets the host path to the persistent, version-independent data directory for a module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <returns>The full host path to the module's persistent data directory (<c>&lt;StorageBasePath&gt;/&lt;moduleId&gt;/data</c>).</returns>
    public string GetModuleDataDirectoryPath(Guid moduleId) =>
        Path.GetFullPath(Path.Combine(_options.StorageBasePath, moduleId.ToString(), "data"));

    /// <summary>
    /// Ensures the persistent, version-independent data directory for a module exists on the host
    /// and has write permissions for container processes.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <returns>The full host path to the module's persistent data directory.</returns>
    public string EnsureModuleDataDirectory(Guid moduleId)
    {
        var dataPath = GetModuleDataDirectoryPath(moduleId);
        try
        {
            Directory.CreateDirectory(dataPath);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    dataPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create persistent data directory '{DataPath}' for module '{ModuleId}'.", dataPath, moduleId);
            throw;
        }

        return dataPath;
    }

    /// <summary>
    /// Deletes the persistent data directory for the specified module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    public void DeleteModuleDataDirectory(Guid moduleId)
    {
        var dataPath = GetModuleDataDirectoryPath(moduleId);
        DeleteModuleDirectory(dataPath, moduleId);
    }
}