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
        var relativeFilePath = Path.Combine("dockerfile-templates", Guid.NewGuid().ToString("N"));
        return _storage.SaveFileAsync(content, relativeFilePath, ct);
    }

    /// <inheritdoc />
    public void Delete(string storagePath)
    {
        var fullPath = _storage.ResolvePath(storagePath);
        var directoryPath = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The storage path must refer to a file.", nameof(storagePath));
        _storage.DeleteDirectory(directoryPath);
    }
}
