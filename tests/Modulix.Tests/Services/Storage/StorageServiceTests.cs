using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Modulix.Services.Storage;

namespace Modulix.Tests.Services.Storage;

[Trait("Category", "Services")]
[Trait("SubCategory", "StorageService")]
public sealed class StorageServiceTests : IDisposable
{
    private readonly string _testRootPath = Path.Combine(Path.GetTempPath(), "Modulix_Storage_Tests_" + Guid.NewGuid().ToString("N"));
    private readonly string _storageRootPath;
    private readonly StorageService _sut;

    public StorageServiceTests()
    {
        _storageRootPath = Path.Combine(_testRootPath, "storage");
        Directory.CreateDirectory(_testRootPath);
        _sut = new StorageService(
            Options.Create(new ModuleSettings { StorageBasePath = _storageRootPath }),
            NullLogger<StorageService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRootPath))
            Directory.Delete(_testRootPath, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SaveFileAsync_NormalUpload_WritesBytesAndReturnsStorageRelativePath()
    {
        var contents = Encoding.UTF8.GetBytes("plain file contents");
        using var source = new MemoryStream(contents);
        var upload = new FormFile(source, 0, source.Length, "content", "Dockerfile");

        var storagePath = await _sut.SaveFileAsync(upload, Path.Combine("templates", "Dockerfile"));

        Assert.Equal("templates/Dockerfile", storagePath);
        Assert.Equal(contents, await File.ReadAllBytesAsync(Path.Combine(_storageRootPath, "templates", "Dockerfile")));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("nested/../../outside.txt")]
    public async Task SaveFileAsync_PathEscapesStorageRoot_RejectsWithoutWritingOutside(string relativePath)
    {
        var upload = CreateUpload("contents");

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SaveFileAsync(upload, relativePath));

        Assert.False(File.Exists(Path.Combine(_testRootPath, "outside.txt")));
        Assert.False(Directory.Exists(_storageRootPath));
    }

    [Fact]
    public async Task SaveFileAsync_RootedPathOutsideStorage_RejectsWithoutWritingOutside()
    {
        var outsidePath = Path.Combine(_testRootPath, "outside.txt");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.SaveFileAsync(CreateUpload("contents"), outsidePath));

        Assert.False(File.Exists(outsidePath));
        Assert.False(Directory.Exists(_storageRootPath));
    }

    [Fact]
    public async Task SaveFileAsync_EmptyUpload_RejectsWithoutCreatingStorageDirectories()
    {
        var upload = CreateUpload(string.Empty);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.SaveFileAsync(upload, Path.Combine("empty", "upload.txt")));

        Assert.False(Directory.Exists(_storageRootPath));
    }

    [Fact]
    public async Task SaveFileAsync_CopyFailsAfterWritingPartialBytes_RemovesFileAndEmptyDirectories()
    {
        var upload = new PartialFailingFormFile(Encoding.UTF8.GetBytes("partial bytes"));
        var relativePath = Path.Combine("partial", "nested", "Dockerfile");

        await Assert.ThrowsAsync<IOException>(() => _sut.SaveFileAsync(upload, relativePath));

        Assert.False(File.Exists(Path.Combine(_storageRootPath, relativePath)));
        Assert.False(Directory.Exists(Path.Combine(_storageRootPath, "partial")));
    }

    [Fact]
    public async Task SaveFileAsync_TargetAlreadyExists_RejectsWithoutOverwritingExistingBytes()
    {
        var existingPath = Path.Combine(_storageRootPath, "templates", "Dockerfile");
        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        await File.WriteAllTextAsync(existingPath, "original");

        await Assert.ThrowsAsync<IOException>(() =>
            _sut.SaveFileAsync(CreateUpload("replacement"), Path.Combine("templates", "Dockerfile")));

        Assert.Equal("original", await File.ReadAllTextAsync(existingPath));
    }

    [Fact]
    public void ResolvePath_OutsideStorageRoot_ThrowsArgumentException()
    {
        var outsidePath = Path.Combine(_testRootPath, "outside.txt");

        Assert.Throws<ArgumentException>(() => _sut.ResolvePath(outsidePath));
    }

    [Fact]
    public void EnsureDirectory_NestedPath_CreatesAndResolvesWithinStorageRoot()
    {
        var fullPath = _sut.EnsureDirectory(Path.Combine("module", "data"));

        Assert.Equal(Path.Combine(_storageRootPath, "module", "data"), fullPath);
        Assert.True(Directory.Exists(fullPath));
    }

    [Fact]
    public void DeleteDirectory_ExistingStoragePath_RemovesTargetAndPreservesSibling()
    {
        var target = Path.Combine(_storageRootPath, "module", "versions", "v1");
        var sibling = Path.Combine(_storageRootPath, "module", "data");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(target, "module.dll"), "module");
        File.WriteAllText(Path.Combine(sibling, "database.db"), "persistent");

        _sut.DeleteDirectory(target);

        Assert.False(Directory.Exists(target));
        Assert.Equal("persistent", File.ReadAllText(Path.Combine(sibling, "database.db")));
    }

    private static IFormFile CreateUpload(string contents)
    {
        var bytes = Encoding.UTF8.GetBytes(contents);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, stream.Length, "content", "upload.txt");
    }

    private sealed class PartialFailingFormFile(byte[] partialBytes) : IFormFile
    {
        public string ContentType => "application/octet-stream";
        public string ContentDisposition => string.Empty;
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public long Length => partialBytes.Length;
        public string Name => "content";
        public string FileName => "partial.bin";

        public Stream OpenReadStream() => new MemoryStream(partialBytes);

        public void CopyTo(Stream target)
        {
            target.Write(partialBytes);
            throw new IOException("Upload stream failed after a partial write.");
        }

        public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            await target.WriteAsync(partialBytes, cancellationToken);
            throw new IOException("Upload stream failed after a partial write.");
        }
    }
}
