using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Modulix.Client.Models.Dtos;
using Modulix.Services;
using Modulix.Services.Interfaces;
using Modulix.Services.Storage;
using Modulix.Services.Storage.Interfaces;
using Modulix.Tests.TestData;

namespace Modulix.Tests.Services;

[Trait("Category", "Services")]
[Trait("SubCategory", "DockerfileTemplateService")]
public sealed class DockerfileTemplateServiceTests : ServiceTestBase
{
    private readonly IDockerfileTemplateService _sut;

    public DockerfileTemplateServiceTests()
    {
        var storage = new StorageService(_moduleOptions, NullLogger<StorageService>.Instance);
        _sut = new DockerfileTemplateService(
            NullLogger<DockerfileTemplateService>.Instance,
            _context,
            CreateTemplateStorage(storage));
    }

    [Fact]
    public async Task CreateDockerfileTemplateAsync_SavesMetadataAndDockerfileContents()
    {
        const string dockerfileContents = "FROM alpine:3.20";

        await _sut.CreateDockerfileTemplateAsync(CreateDto("Initial Dockerfile", dockerfileContents));

        var stored = await _context.DockerfileTemplates.AsNoTracking().SingleAsync();
        var filePath = GetStoredFilePath(stored.StoragePath);
        Assert.Equal("Initial Dockerfile", stored.FileName);
        Assert.Equal("initial description", stored.Description);
        Assert.StartsWith("dockerfile-templates/", stored.StoragePath);
        Assert.Equal("Dockerfile", Path.GetFileName(stored.StoragePath));
        Assert.Equal(dockerfileContents, await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task CreateDockerfileTemplateAsync_DatabaseSaveFails_DeletesSavedFileAndDoesNotPersistMetadata()
    {
        string? savedFilePath = null;
        _saveInterceptor.BeforeSave = () =>
        {
            savedFilePath = Assert.Single(Directory.GetFiles(_moduleOptions.Value.StorageBasePath, "Dockerfile", SearchOption.AllDirectories));
            Assert.Equal("contents before database save", File.ReadAllText(savedFilePath));
        };
        _saveInterceptor.ExceptionToThrow = new InvalidOperationException("Database save failed.");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.CreateDockerfileTemplateAsync(CreateDto("Failed create", "contents before database save")));

        Assert.Equal("Database save failed.", exception.Message);
        Assert.Empty(await _context.DockerfileTemplates.ToListAsync());
        Assert.NotNull(savedFilePath);
        Assert.False(File.Exists(savedFilePath));
        AssertNoTemplateStorageFiles();
    }

    [Fact]
    public async Task UpdateDockerfileTemplateAsync_Success_ReplacesMetadataAndFile()
    {
        await _sut.CreateDockerfileTemplateAsync(CreateDto("Before update", "old Dockerfile"));
        var before = await _context.DockerfileTemplates.SingleAsync();
        var oldStoragePath = before.StoragePath;
        var oldPath = GetStoredFilePath(before.StoragePath);

        await _sut.UpdateDockerfileTemplateAsync(
            before.Id,
            new UpdateDockerfileTemplateDto
            {
                FileName = "After update",
                Description = "updated description",
                Content = CreateFormFile("new Dockerfile")
            });

        var after = await _context.DockerfileTemplates.AsNoTracking().SingleAsync();
        Assert.Equal("After update", after.FileName);
        Assert.Equal("updated description", after.Description);
        Assert.NotEqual(oldStoragePath, after.StoragePath);
        Assert.Equal("new Dockerfile", await File.ReadAllTextAsync(GetStoredFilePath(after.StoragePath)));
        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    public async Task UpdateDockerfileTemplateAsync_DatabaseSaveFails_RestoresMetadataAndKeepsOnlyOriginalFile()
    {
        await _sut.CreateDockerfileTemplateAsync(CreateDto("Original name", "original Dockerfile"));
        var original = await _context.DockerfileTemplates.SingleAsync();
        var originalPath = GetStoredFilePath(original.StoragePath);
        string? updatedPath = null;
        _saveInterceptor.BeforeSave = () =>
        {
            updatedPath = Assert.Single(
                Directory.GetFiles(_moduleOptions.Value.StorageBasePath, "Dockerfile", SearchOption.AllDirectories),
                path => !string.Equals(path, originalPath, StringComparison.Ordinal));
            Assert.Equal("replacement Dockerfile", File.ReadAllText(updatedPath));
            Assert.True(File.Exists(originalPath));
        };
        _saveInterceptor.ExceptionToThrow = new InvalidOperationException("Database save failed.");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.UpdateDockerfileTemplateAsync(
                original.Id,
                new UpdateDockerfileTemplateDto
                {
                    FileName = "Failed replacement",
                    Description = "uncommitted description",
                    Content = CreateFormFile("replacement Dockerfile")
                }));

        Assert.Equal("Database save failed.", exception.Message);
        var persisted = await _context.DockerfileTemplates.AsNoTracking().SingleAsync();
        Assert.Equal("Original name", persisted.FileName);
        Assert.Equal("initial description", persisted.Description);
        Assert.Equal(original.StoragePath, persisted.StoragePath);
        Assert.Equal("original Dockerfile", await File.ReadAllTextAsync(originalPath));
        Assert.NotNull(updatedPath);
        Assert.False(File.Exists(updatedPath));
        Assert.Single(Directory.GetFiles(_moduleOptions.Value.StorageBasePath, "Dockerfile", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DeleteDockerfileTemplateAsync_Success_RemovesMetadataAndStoredFile()
    {
        await _sut.CreateDockerfileTemplateAsync(CreateDto("Delete me", "file to remove"));
        var stored = await _context.DockerfileTemplates.SingleAsync();
        var filePath = GetStoredFilePath(stored.StoragePath);

        await _sut.DeleteDockerfileTemplateAsync(stored.Id);

        Assert.Empty(await _context.DockerfileTemplates.AsNoTracking().ToListAsync());
        Assert.False(File.Exists(filePath));
        AssertNoTemplateStorageFiles();
    }

    [Fact]
    public async Task DeleteDockerfileTemplateAsync_DatabaseSaveFails_PreservesMetadataAndStoredFile()
    {
        await _sut.CreateDockerfileTemplateAsync(CreateDto("Keep me", "file to preserve"));
        var stored = await _context.DockerfileTemplates.SingleAsync();
        var filePath = GetStoredFilePath(stored.StoragePath);
        _saveInterceptor.ExceptionToThrow = new InvalidOperationException("Database save failed.");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.DeleteDockerfileTemplateAsync(stored.Id));

        Assert.Equal("Database save failed.", exception.Message);
        var persisted = await _context.DockerfileTemplates.AsNoTracking().SingleAsync();
        Assert.Equal("Keep me", persisted.FileName);
        Assert.Equal(stored.StoragePath, persisted.StoragePath);
        Assert.Equal("file to preserve", await File.ReadAllTextAsync(filePath));
    }

    private string GetStoredFilePath(string storagePath) =>
        Path.Combine(_moduleOptions.Value.StorageBasePath, storagePath.Replace('/', Path.DirectorySeparatorChar));

    private void AssertNoTemplateStorageFiles()
    {
        if (!Directory.Exists(_moduleOptions.Value.StorageBasePath))
            return;

        Assert.Empty(Directory.GetFiles(_moduleOptions.Value.StorageBasePath, "*", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetDirectories(_moduleOptions.Value.StorageBasePath, "*", SearchOption.AllDirectories));
    }

    private static CreateDockerfileTemplateDto CreateDto(string fileName, string contents) => new()
    {
        FileName = fileName,
        Description = "initial description",
        Content = CreateFormFile(contents)
    };

    private static FormFile CreateFormFile(string contents)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(contents));
        return new FormFile(stream, 0, stream.Length, "content", "Dockerfile");
    }

    private static IDockerfileTemplateStorageService CreateTemplateStorage(IStorageService storage)
    {
        var implementationType = typeof(StorageService).Assembly.GetType(
            "Modulix.Services.Storage.DockerfileTemplateStorageService",
            throwOnError: true)!;
        return (IDockerfileTemplateStorageService)Activator.CreateInstance(implementationType, storage)!;
    }
}
