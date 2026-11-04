namespace Modulix.Services.Storage.Interfaces;

/// <summary>
/// Provides storage operations for reusable Dockerfile templates.
/// </summary>
public interface IDockerfileTemplateStorageService
{
    /// <summary>
    /// Saves uploaded Dockerfile contents as dockerfile-templates/&lt;GUID&gt;/&lt;uploaded-file-name&gt;.
    /// Only the base file name is retained; any client-provided directories are removed.
    /// </summary>
    /// <param name="content">The uploaded Dockerfile content.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The storage-relative path including the saved file name.</returns>
    /// <exception cref="ArgumentException">Thrown if the uploaded file name is invalid.</exception>
    Task<string> SaveAsync(IFormFile content, CancellationToken ct = default);

    /// <summary>
    /// Deletes only the selected template file and any empty parent directories.
    /// </summary>
    /// <param name="storagePath">The storage path of the Dockerfile template to delete.</param>
    /// <exception cref="ArgumentException">Thrown if the storage path is invalid or outside the storage root.</exception>
    void Delete(string storagePath);
}
