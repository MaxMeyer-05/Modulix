namespace Modulix.Services.Storage.Interfaces;

/// <summary>
/// Provides storage operations for reusable Dockerfile templates.
/// </summary>
public interface IDockerfileTemplateStorageService
{
    /// <summary>
    /// Saves uploaded Dockerfile contents under a unique storage-relative path.
    /// </summary>
    Task<string> SaveAsync(IFormFile content, CancellationToken ct = default);

    /// <summary>
    /// Deletes the file and its template-specific directory.
    /// </summary>
    void Delete(string storagePath);
}
