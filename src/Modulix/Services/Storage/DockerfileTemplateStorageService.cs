using Modulix.Services.Storage.Interfaces;

namespace Modulix.Services.Storage;

/// <inheritdoc cref="IDockerfileTemplateStorageService"/>
internal sealed class DockerfileTemplateStorageService : IDockerfileTemplateStorageService
{
    /// <summary>
    /// The shared storage service.
    /// </summary>
    private readonly IStorageService _storage;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerfileTemplateStorageService"/> class.
    /// </summary>
    /// <param name="storage">The shared storage service.</param>
    public DockerfileTemplateStorageService(IStorageService storage)
    {
        _storage = storage;
    }

    /// <inheritdoc />
    public Task<string> SaveAsync(IFormFile content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.FileName, nameof(content));
        var fileName = Path.GetFileName(content.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName is "." or ".." ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The uploaded file must have a valid file name.", nameof(content));
        }

        var relativeFilePath = Path.Combine("dockerfile-templates", Guid.NewGuid().ToString("N"), fileName);
        return _storage.SaveFileAsync(content, relativeFilePath, ct);
    }

    /// <inheritdoc />
    public void Delete(string storagePath)
    {
        _storage.DeleteFile(storagePath);
    }
}
