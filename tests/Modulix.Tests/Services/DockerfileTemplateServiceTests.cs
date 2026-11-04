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
        var storage = new StorageService(_storageOptions, NullLogger<StorageService>.Instance);
        _sut = new DockerfileTemplateService(
            NullLogger<DockerfileTemplateService>.Instance,
            _context,
            CreateTemplateStorage(storage));
    }

    [Theory]
    [InlineData("Dockerfile", "Dockerfile")]
    [InlineData("Dockerfile.alpine", "Dockerfile.alpine")]
    [InlineData(@"C:\fakepath\Dockerfile.dev", "Dockerfile.dev")]
    [InlineData("../../outside/Dockerfile.prod", "Dockerfile.prod")]
    public async Task CreateDockerfileTemplateAsync_SavesMetadataAndDockerfileContents(
        string uploadedFileName, string expectedFileName)
    {
        const string dockerfileContents = "FROM alpine:3.20";

        await _sut.CreateDockerfileTemplateAsync(CreateDto("Initial Dockerfile", dockerfileContents, uploadedFileName));

        var stored = await _context.DockerfileTemplates.AsNoTracking().SingleAsync();
        var filePath = GetStoredFilePath(stored.StoragePath);
        Assert.Equal("Initial Dockerfile", stored.FileName);
        Assert.Equal("initial description", stored.Description);
        Assert.StartsWith("dockerfile-templates/", stored.StoragePath);
        var templateId = Path.GetFileName(Path.GetDirectoryName(filePath));
        Assert.True(Guid.TryParseExact(templateId, "N", out _));
        Assert.NotNull(templateId);
        Assert.Equal($"dockerfile-templates/{templateId}/{expectedFileName}", stored.StoragePath);
        Assert.Equal(Path.Combine(_storageOptions.Value.StorageBasePath, "dockerfile-templates", templateId, expectedFileName), filePath);
        Assert.False(Directory.Exists(Path.Combine(ModuleStoragePath, "dockerfile-templates")));
        Assert.Equal(dockerfileContents, await File.ReadAllTextAsync(filePath));
        Assert.Equal(filePath, Assert.Single(
            Directory.GetFiles(_storageOptions.Value.StorageBasePath, "*", SearchOption.AllDirectories)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("folder/")]
    [InlineData("folder\\")]
    public async Task CreateDockerfileTemplateAsync_InvalidUploadedFileName_RejectsWithoutSaving(string uploadedFileName)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateDockerfileTemplateAsync(CreateDto("Invalid upload", "FROM alpine:3.20", uploadedFileName)));

        Assert.Empty(await _context.DockerfileTemplates.AsNoTracking().ToListAsync());
        AssertNoTemplateStorageFiles();
    }

    [Fact]
    public async Task CreateDockerfileTemplateAsync_DatabaseSaveFails_DeletesSavedFileAndDoesNotPersistMetadata()
    {
        string? savedFilePath = null;
        _saveInterceptor.BeforeSave = () =>
        {
            savedFilePath = Assert.Single(Directory.GetFiles(_storageOptions.Value.StorageBasePath, "*", SearchOption.AllDirectories));
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
    public async Task CreateDockerfileTemplateAsync_DatabaseSaveFails_PreservesExistingTemplate()
    {
        await _sut.CreateDockerfileTemplateAsync(CreateDto("Existing template", "existing Dockerfile"));
        var original = await _context.DockerfileTemplates.AsNoTracking().SingleAsync();
        var originalPath = GetStoredFilePath(original.StoragePath);
        _saveInterceptor.ExceptionToThrow = new InvalidOperationException("Database save failed.");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.CreateDockerfileTemplateAsync(CreateDto("Failed create", "uncommitted Dockerfile")));

        Assert.Equal("Database save failed.", exception.Message);
        var persisted = await _context.DockerfileTemplates.AsNoTracking().SingleAsync();
        Assert.Equal(original.Id, persisted.Id);
        Assert.Equal(original.StoragePath, persisted.StoragePath);
        Assert.Equal("existing Dockerfile", await File.ReadAllTextAsync(originalPath));
        Assert.Equal(originalPath, Assert.Single(
            Directory.GetFiles(_storageOptions.Value.StorageBasePath, "*", SearchOption.AllDirectories)));
        Assert.Equal(Path.GetDirectoryName(originalPath), Assert.Single(
            Directory.GetDirectories(Path.Combine(_storageOptions.Value.StorageBasePath, "dockerfile-templates"))));
    }

    [Fact]
    public async Task UpdateDockerfileTemplateAsync_Success_ReplacesMetadataAndFile()
    {
        await _sut.CreateDockerfileTemplateAsync(CreateDto("Before update", "old Dockerfile"));
        var before = await _context.DockerfileTemplates.SingleAsync();
        var oldStoragePath = before.StoragePath;
        var oldPath = GetStoredFilePath(before.StoragePath);
        await _sut.CreateDockerfileTemplateAsync(CreateDto("Other template", "unrelated Dockerfile"));
        var other = await _context.DockerfileTemplates.AsNoTracking().SingleAsync(e => e.Id != before.Id);

        await _sut.UpdateDockerfileTemplateAsync(
            before.Id,
            new UpdateDockerfileTemplateDto
            {
                FileName = "After update",
                Description = "updated description",
                Content = CreateFormFile("new Dockerfile")
            });

        var after = await _context.DockerfileTemplates.AsNoTracking().SingleAsync(e => e.Id == before.Id);
        Assert.Equal("After update", after.FileName);
        Assert.Equal("updated description", after.Description);
        Assert.NotEqual(oldStoragePath, after.StoragePath);
        Assert.Equal("new Dockerfile", await File.ReadAllTextAsync(GetStoredFilePath(after.StoragePath)));
        Assert.False(File.Exists(oldPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(oldPath)));
        Assert.Equal("unrelated Dockerfile", await File.ReadAllTextAsync(GetStoredFilePath(other.StoragePath)));
        Assert.Equal(2, await _context.DockerfileTemplates.CountAsync());
        Assert.Equal(2, Directory.GetFiles(_storageOptions.Value.StorageBasePath, "*", SearchOption.AllDirectories).Length);
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
                Directory.GetFiles(_storageOptions.Value.StorageBasePath, "*", SearchOption.AllDirectories),
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
        Assert.False(Directory.Exists(Path.GetDirectoryName(updatedPath)));
        Assert.Equal(originalPath, Assert.Single(
            Directory.GetFiles(_storageOptions.Value.StorageBasePath, "*", SearchOption.AllDirectories)));
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
    public async Task DeleteDockerfileTemplateAsync_MultipleTemplates_PreservesOtherTemplate()
    {
        await _sut.CreateDockerfileTemplateAsync(CreateDto("Delete me", "file to remove"));
        var target = await _context.DockerfileTemplates.AsNoTracking().SingleAsync();
        var targetPath = GetStoredFilePath(target.StoragePath);
        await _sut.CreateDockerfileTemplateAsync(CreateDto("Keep me", "file to preserve"));
        var other = await _context.DockerfileTemplates.AsNoTracking().SingleAsync(e => e.Id != target.Id);
        var otherPath = GetStoredFilePath(other.StoragePath);

        await _sut.DeleteDockerfileTemplateAsync(target.Id);

        var remaining = await _context.DockerfileTemplates.AsNoTracking().SingleAsync();
        Assert.Equal(other.Id, remaining.Id);
        Assert.Equal(other.StoragePath, remaining.StoragePath);
        Assert.Equal("file to preserve", await File.ReadAllTextAsync(otherPath));
        Assert.False(File.Exists(targetPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(targetPath)));
        Assert.Equal(otherPath, Assert.Single(
            Directory.GetFiles(_storageOptions.Value.StorageBasePath, "*", SearchOption.AllDirectories)));
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
        Path.Combine(_storageOptions.Value.StorageBasePath, storagePath.Replace('/', Path.DirectorySeparatorChar));

    private void AssertNoTemplateStorageFiles()
    {
        if (!Directory.Exists(_storageOptions.Value.StorageBasePath))
            return;

        Assert.Empty(Directory.GetFiles(_storageOptions.Value.StorageBasePath, "*", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetDirectories(_storageOptions.Value.StorageBasePath, "*", SearchOption.AllDirectories));
    }

    private static CreateDockerfileTemplateDto CreateDto(
        string fileName, string contents, string uploadedFileName = "Dockerfile") => new()
    {
        FileName = fileName,
        Description = "initial description",
        Content = CreateFormFile(contents, uploadedFileName)
    };

    private static FormFile CreateFormFile(string contents, string fileName = "Dockerfile")
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(contents));
        return new FormFile(stream, 0, stream.Length, "content", fileName);
    }

    private static IDockerfileTemplateStorageService CreateTemplateStorage(IStorageService storage)
    {
        var implementationType = typeof(StorageService).Assembly.GetType(
            "Modulix.Services.Storage.DockerfileTemplateStorageService",
            throwOnError: true)!;
        return (IDockerfileTemplateStorageService)Activator.CreateInstance(implementationType, storage)!;
    }
}
