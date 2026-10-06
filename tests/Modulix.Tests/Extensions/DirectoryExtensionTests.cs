using System.IO.Compression;
using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Modulix.Extensions;
using Modulix.Models.Options;

namespace Modulix.Tests.Extensions;

[Trait("Category", "Extensions")]
[Trait("SubCategory", "DirectoryExtension")]
public class DirectoryExtensionTests : IDisposable
{
    private readonly string _testRootDirectory = Path.Combine(Path.GetTempPath(), "Modulix_Directory_Tests_" + Guid.NewGuid().ToString("N"));
    private readonly ModulixOptions _options;
    private readonly DirectoryExtension _sut;

    #region Setup & Teardown

    public DirectoryExtensionTests()
    {
        Directory.CreateDirectory(_testRootDirectory);
        _options = new ModulixOptions { StorageBasePath = Path.Combine(_testRootDirectory, "modules") };
        _sut = new DirectoryExtension(NullLogger<DirectoryExtension>.Instance, Options.Create(_options));
    }

    public void Dispose()
    {
        Directory.Delete(_testRootDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region ValidateModuleArchive Tests

    [Theory]
    [InlineData(2, 4, true)]
    [InlineData(1, 4, false)]
    [InlineData(2, 3, false)]
    public void ValidateModuleArchive_ConfiguredLimits_AcceptsExactBoundaryAndRejectsExcess(
        int maximumEntries, long maximumBytes, bool valid)
    {
        _options.MaximumArchiveEntryCount = maximumEntries;
        _options.MaximumArchiveUncompressedBytes = maximumBytes;
        var archivePath = Path.Combine(_testRootDirectory, "module.zip");
        using (var contents = CreateArchive(("first.dll", "ab"), ("nested/second.json", "cd")))
        using (var file = File.Create(archivePath))
            contents.CopyTo(file);

        if (valid)
            _sut.ValidateModuleArchive(archivePath);
        else
            Assert.Throws<InvalidOperationException>(() => _sut.ValidateModuleArchive(archivePath));

        Assert.True(File.Exists(archivePath));
    }

    #endregion

    #region CreateModuleTargetPath Tests

    [Fact]
    public async Task CreateModuleTargetPathAsync_ValidArchive_ExtractsContentsAndReplacesOnlyTargetModule()
    {
        var moduleId = Guid.NewGuid();
        var targetPath = Path.Combine(_options.StorageBasePath, moduleId.ToString());
        Directory.CreateDirectory(targetPath);
        await File.WriteAllTextAsync(Path.Combine(targetPath, "obsolete.dll"), "old");
        var otherModulePath = Path.Combine(_options.StorageBasePath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(otherModulePath);
        var otherModuleFile = Path.Combine(otherModulePath, "keep.dll");
        await File.WriteAllTextAsync(otherModuleFile, "keep");
        using var contents = CreateArchive(("module.dll", "assembly"), ("config/settings.json", "configuration"));

        var result = await _sut.CreateModuleTargetPathAsync(moduleId, CreateFormFile(contents, "MODULE.ZIP"));

        Assert.Equal(targetPath, result);
        Assert.Equal("assembly", await File.ReadAllTextAsync(Path.Combine(result, "module.dll")));
        Assert.Equal("configuration", await File.ReadAllTextAsync(Path.Combine(result, "config/settings.json")));
        Assert.False(File.Exists(Path.Combine(result, "obsolete.dll")));
        Assert.Equal("keep", await File.ReadAllTextAsync(otherModuleFile));
    }

    [Theory]
    [InlineData("module.zip", "")]
    [InlineData("module.dll", "not a zip")]
    public async Task CreateModuleTargetPathAsync_InvalidUpload_PreservesExistingDirectory(string fileName, string payload)
    {
        var moduleId = Guid.NewGuid();
        var targetPath = Path.Combine(_options.StorageBasePath, moduleId.ToString());
        Directory.CreateDirectory(targetPath);
        var existingFile = Path.Combine(targetPath, "keep.dll");
        await File.WriteAllTextAsync(existingFile, "keep");
        using var contents = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateModuleTargetPathAsync(moduleId, CreateFormFile(contents, fileName)));

        Assert.Equal("keep", await File.ReadAllTextAsync(existingFile));
    }

    [Fact]
    public async Task CreateModuleTargetPathAsync_StoragePathIsFile_PropagatesFailureAndPreservesExistingFile()
    {
        await File.WriteAllTextAsync(_options.StorageBasePath, "existing file");
        var moduleId = Guid.NewGuid();
        using var contents = CreateArchive(("module.dll", "assembly"));

        await Assert.ThrowsAnyAsync<IOException>(() =>
            _sut.CreateModuleTargetPathAsync(moduleId, CreateFormFile(contents)));

        Assert.Equal("existing file", await File.ReadAllTextAsync(_options.StorageBasePath));
        Assert.False(Directory.Exists(Path.Combine(_options.StorageBasePath, moduleId.ToString())));
    }

    [Fact]
    public async Task CreateModuleTargetPathAsync_CorruptArchive_RemovesIncompleteTarget()
    {
        var moduleId = Guid.NewGuid();
        using var contents = new MemoryStream(Encoding.UTF8.GetBytes("not a zip"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _sut.CreateModuleTargetPathAsync(moduleId, CreateFormFile(contents)));

        Assert.False(Directory.Exists(Path.Combine(_options.StorageBasePath, moduleId.ToString())));
    }

    [Fact]
    public async Task CreateModuleTargetPathAsync_ArchiveExceedsLimit_DoesNotLeaveExtractedFiles()
    {
        _options.MaximumArchiveUncompressedBytes = 3;
        var moduleId = Guid.NewGuid();
        using var contents = CreateArchive(("module.dll", "four"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.CreateModuleTargetPathAsync(moduleId, CreateFormFile(contents)));

        Assert.False(Directory.Exists(Path.Combine(_options.StorageBasePath, moduleId.ToString())));
    }

    [Theory]
    [InlineData("../escaped.dll")]
    [InlineData("nested/../../escaped.dll")]
    public async Task CreateModuleTargetPathAsync_PathTraversal_DoesNotWriteOutsideTargetAndCleansPartialExtraction(string entryName)
    {
        var moduleId = Guid.NewGuid();
        using var contents = CreateArchive(("valid.dll", "valid"), (entryName, "malicious"));

        await Assert.ThrowsAsync<IOException>(() =>
            _sut.CreateModuleTargetPathAsync(moduleId, CreateFormFile(contents)));

        Assert.False(File.Exists(Path.Combine(_options.StorageBasePath, "escaped.dll")));
        Assert.False(Directory.Exists(Path.Combine(_options.StorageBasePath, moduleId.ToString())));
    }

    [Fact]
    public async Task CreateModuleTargetPathAsync_CancelledUpload_RemovesIncompleteTarget()
    {
        var moduleId = Guid.NewGuid();
        using var contents = CreateArchive(("module.dll", "assembly"));
        var cancellationToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.CreateModuleTargetPathAsync(moduleId, CreateFormFile(contents), cancellationToken));

        Assert.False(Directory.Exists(Path.Combine(_options.StorageBasePath, moduleId.ToString())));
    }

    #endregion

    #region Cleanup Tests

    [Fact]
    public void DeleteTemporaryFile_ExistingOrMissingFile_IsIdempotentAndPreservesOtherFiles()
    {
        var temporaryFile = Path.Combine(_testRootDirectory, "temporary.zip");
        var otherFile = Path.Combine(_testRootDirectory, "keep.zip");
        File.WriteAllText(temporaryFile, "temporary");
        File.WriteAllText(otherFile, "keep");

        _sut.DeleteTemporaryFile(temporaryFile, Guid.NewGuid());
        _sut.DeleteTemporaryFile(temporaryFile, Guid.NewGuid());

        Assert.False(File.Exists(temporaryFile));
        Assert.Equal("keep", File.ReadAllText(otherFile));
    }

    [Fact]
    public void DeleteModuleDirectory_NestedOrMissingDirectory_IsIdempotentAndPreservesOtherDirectories()
    {
        var targetPath = Path.Combine(_testRootDirectory, "target");
        Directory.CreateDirectory(Path.Combine(targetPath, "nested"));
        File.WriteAllText(Path.Combine(targetPath, "nested", "module.dll"), "module");
        var otherPath = Path.Combine(_testRootDirectory, "keep");
        Directory.CreateDirectory(otherPath);

        _sut.DeleteModuleDirectory(targetPath, Guid.NewGuid());
        _sut.DeleteModuleDirectory(targetPath, Guid.NewGuid());

        Assert.False(Directory.Exists(targetPath));
        Assert.True(Directory.Exists(otherPath));
    }

    #endregion

    #region Helper Methods

    private static FormFile CreateFormFile(Stream contents, string fileName = "module.zip") =>
        new(contents, 0, contents.Length, "ModuleFile", fileName);

    private static MemoryStream CreateArchive(params (string Name, string Contents)[] entries)
    {
        var contents = new MemoryStream();
        using (var archive = new ZipArchive(contents, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(entry.Name).Open(), new UTF8Encoding(false));
                writer.Write(entry.Contents);
            }
        }
        contents.Position = 0;
        return contents;
    }

    #endregion
}