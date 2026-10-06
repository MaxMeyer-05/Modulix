using System.IO.Compression;
using Microsoft.Extensions.Options;

using Modulix.Models.Options;

namespace Modulix.Extensions;

/// <summary>
/// Provides utility methods for handling module directories and archives.
/// </summary>
public class DirectoryExtension
{
    /// <summary>
    /// The shared module settings.
    /// </summary>
    private readonly ModulixOptions _options;

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
        IOptions<ModulixOptions> options)
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete module directory for module with ID '{ModuleId}'.", moduleId);
        }
    }

        /// <summary>
    /// Creates the target path for a module by extracting its files from the provided .zip archive.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="file">The .zip archive containing the module files.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The path to the extracted module files.</returns>
    public async Task<string> CreateModuleTargetPathAsync(Guid moduleId, IFormFile file, CancellationToken ct = default)
    {
        var targetPath = Path.Combine(_options.StorageBasePath, moduleId.ToString());
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
            ZipFile.ExtractToDirectory(tmpZipPath, targetPath, true);
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
}