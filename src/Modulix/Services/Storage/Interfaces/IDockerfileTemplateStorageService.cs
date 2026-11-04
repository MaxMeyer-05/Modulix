namespace Modulix.Services.Storage.Interfaces;

/// <summary>
/// Provides storage operations for reusable Dockerfile templates.
/// </summary>
public interface IDockerfileTemplateStorageService
{
    /// <summary>
    /// Saves uploaded Dockerfile contents under a unique storage-relative path.
    /// </summary>
    /// <param name="content">The uploaded Dockerfile content.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The storage path of the saved Dockerfile template.</returns>
    Task<string> SaveAsync(IFormFile content, CancellationToken ct = default);

    /// <summary>
    /// Deletes the file and its template-specific directory.
    /// </summary>
    /// <param name="storagePath">The storage path of the Dockerfile template to delete.</param>
    /// <exception cref="ArgumentException">Thrown if the storage path does not refer to a valid file.</exception>
    void Delete(string storagePath);
}
