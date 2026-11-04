using Modulix.Services.Storage.Interfaces;

namespace Modulix.Services.Storage;

internal sealed class DockerfileTemplateStorageService : IDockerfileTemplateStorageService
{
    private readonly IStorageService _storage;

    public DockerfileTemplateStorageService(IStorageService storage)
    {
        _storage = storage;
    }

    public Task<string> SaveAsync(IFormFile content, CancellationToken ct = default)
    {
        var relativeFilePath = Path.Combine("dockerfile-templates", Guid.NewGuid().ToString("N"), "Dockerfile");
        return _storage.SaveFileAsync(content, relativeFilePath, ct);
    }

    public void Delete(string storagePath)
    {
        var fullPath = _storage.ResolvePath(storagePath);
        var directoryPath = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The storage path must refer to a file.", nameof(storagePath));
        _storage.DeleteDirectory(directoryPath);
    }
}
