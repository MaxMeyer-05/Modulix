using System.IO.Compression;
using Microsoft.Extensions.Options;

using Modulix.Services.Storage.Interfaces;

namespace Modulix.Services.Storage;

/// <inheritdoc cref="IModuleStorageService"/>
public class ModuleStorageService : IModuleStorageService
{
    /// <summary>
    /// The shared module settings.
    /// </summary>
    private readonly ModuleSettings _options;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<ModuleStorageService> _logger;

    /// <summary>
    /// The shared storage service.
    /// </summary>
    private readonly IStorageService _storageService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleStorageService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="options">The shared module settings.</param>
    /// <param name="storageService">The shared storage service.</param>
    public ModuleStorageService(
        ILogger<ModuleStorageService> logger, 
        IOptions<ModuleSettings> options,
        IStorageService storageService)
    {
        _logger = logger;
        _options = options.Value;
        _storageService = storageService;
    }

    /// <summary>
    /// Validates the contents of a module archive.
    /// </summary>
    /// <param name="archivePath">The path to the module archive to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown if the module archive exceeds the allowed limits.</exception>
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
    /// <param name="moduleId">Optional - The ID of the module associated with the temporary file.</param>
    public void DeleteTemporaryFile(string filePath, Guid? moduleId = null)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            if (moduleId is not null)
                _logger.LogWarning(ex, "Failed to delete temporary ZIP file for module with ID '{ModuleId}'.", moduleId);
            else
                _logger.LogWarning(ex, "Failed to delete file.");
        }
    }

    /// <inheritdoc />
    public void DeleteDirectory(string directoryPath, Guid? moduleId = null)
    {
        _storageService.DeleteDirectory(directoryPath, moduleId?.ToString());
    }

    /// <inheritdoc />
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

        if (file.Length == 0)
            throw new ArgumentException("Module file cannot be empty.");
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Module file must be a .zip archive.");

        var relativeTargetPath = Path.Combine("modules", moduleId.ToString(), "versions", normalizedVersion);
        return await _storageService.CreateDirectoryAsync(
            relativeTargetPath,
            async (targetPath, token) =>
            {
                var tmpZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
                try
                {
                    await using (var stream = new FileStream(tmpZipPath, FileMode.CreateNew))
                        await file.CopyToAsync(stream, token);

                    ValidateModuleArchive(tmpZipPath);
                    ExtractModuleArchive(tmpZipPath, targetPath);
                    EnsureModuleDataDirectory(moduleId);
                    _logger.LogInformation("Module files extracted to '{TargetPath}'.", targetPath);
                }
                finally
                {
                    DeleteTemporaryFile(tmpZipPath, moduleId);
                }
            },
            ct);
    }

    /// <inheritdoc />
    public string GetModuleDataDirectoryPath(Guid moduleId) =>
        _storageService.ResolvePath(Path.Combine("modules", moduleId.ToString(), "data"));

    /// <inheritdoc />
    public string EnsureModuleDataDirectory(Guid moduleId)
    {
        var dataPath = GetModuleDataDirectoryPath(moduleId);
        try
        {
            _storageService.EnsureDirectory(Path.Combine("modules", moduleId.ToString(), "data"), writableForContainers: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create persistent data directory '{DataPath}' for module '{ModuleId}'.", dataPath, moduleId);
            throw;
        }

        return dataPath;
    }

    /// <inheritdoc />
    public void DeleteModuleDataDirectory(Guid moduleId)
    {
        var dataPath = GetModuleDataDirectoryPath(moduleId);
        DeleteDirectory(dataPath, moduleId);
    }
}