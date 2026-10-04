using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Server.Services;

/// <summary>
/// Unit tests for <see cref="ModuleEndpointScanner"/>.
/// </summary>
[Trait("Category", "Services")]
[Trait("SubCategory", "ModuleEndpointScanner")]
public class ModuleEndpointScannerTests : IDisposable
{
    private readonly string _testRootDirectory;
    private readonly ModuleEndpointScanner _sut;

    #region Setup & Teardown

    public ModuleEndpointScannerTests()
    {
        _testRootDirectory = Path.Combine(Path.GetTempPath(), "Modulix_Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRootDirectory);
        _sut = new ModuleEndpointScanner(NullLogger<ModuleEndpointScanner>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRootDirectory))
        {
            try
            {
                Directory.Delete(_testRootDirectory, recursive: true);
            }
            catch
            {
                // Ignored during cleanup
            }
        }

        GC.SuppressFinalize(this);
    }

    #endregion

    #region ScanDirectory Tests

    [Fact]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_DirectoryDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var missingDirectory = Path.Combine(_testRootDirectory, "missing");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            _sut.ScanDirectoryAsync(missingDirectory));

        Assert.Equal($"The specified module directory does not exist: {missingDirectory}", exception.Message);
    }

    [Fact]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_NoRuntimeConfigFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var moduleDirectory = CreateModuleDirectory("missing-runtime-config");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _sut.ScanDirectoryAsync(moduleDirectory));

        Assert.Equal("No runtime config files found for the module.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_RuntimeConfigWithoutMatchingAssembly_ThrowsFileNotFoundException()
    {
        // Arrange
        var moduleDirectory = CreateModuleDirectory("missing-entry-assembly");
        await File.WriteAllTextAsync(Path.Combine(moduleDirectory, "module.runtimeconfig.json"), "{}");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _sut.ScanDirectoryAsync(moduleDirectory));

        Assert.Equal("No entry assembly found for the module.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var moduleDirectory = CreateModuleDirectory("cancelled");
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.ScanDirectoryAsync(moduleDirectory, cancellationTokenSource.Token));
    }

    [Fact]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_AssemblyWithController_ReturnsResolvedEndpoints()
    {
        // Arrange
        var moduleDirectory = CreateModuleDirectoryFromTestOutput();

        // Act
        var result = await _sut.ScanDirectoryAsync(moduleDirectory);

        // Assert
        Assert.Equal(Path.GetFileName(typeof(ModuleEndpointScannerTests).Assembly.Location), result.EntryAssemblyFileName);
        Assert.Contains(result.DiscoveredEndpoints, endpoint =>
            endpoint.HttpMethod == "GET" && endpoint.EndpointPath == "/api/ModuleEndpointScannerSample");
        Assert.Contains(result.DiscoveredEndpoints, endpoint =>
            endpoint.HttpMethod == "POST" && endpoint.EndpointPath == "/api/ModuleEndpointScannerSample/items/Create");
        Assert.Contains(result.DiscoveredEndpoints, endpoint =>
            endpoint.HttpMethod == "PUT" && endpoint.EndpointPath == "/absolute");
        Assert.Contains(result.DiscoveredEndpoints, endpoint =>
            endpoint.HttpMethod == "DELETE" && endpoint.EndpointPath == "/root/Remove");
        Assert.Contains(result.DiscoveredEndpoints, endpoint =>
            endpoint.HttpMethod == "PATCH" && endpoint.EndpointPath == "/api/ModuleEndpointScannerSample/multi");
        Assert.Contains(result.DiscoveredEndpoints, endpoint =>
            endpoint.HttpMethod == "OPTIONS" && endpoint.EndpointPath == "/api/ModuleEndpointScannerSample/multi");
    }

    [Fact]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_MultipleRuntimeConfigsAndRouteVariants_ReturnsAllDistinctEndpoints()
    {
        // Arrange
        var moduleDirectory = CreateModuleDirectoryFromTestOutput();
        await File.WriteAllTextAsync(Path.Combine(moduleDirectory, "unmatched.runtimeconfig.json"), "{}");

        // Act
        var result = await _sut.ScanDirectoryAsync(moduleDirectory);

        // Assert
        Assert.Contains(result.DiscoveredEndpoints, endpoint =>
            endpoint.HttpMethod == "GET" && endpoint.EndpointPath == "/action-only/Details");
        Assert.Contains(result.DiscoveredEndpoints, endpoint =>
            endpoint.HttpMethod == "GET" && endpoint.EndpointPath == "/api/route-variants/status");
        Assert.Contains(result.DiscoveredEndpoints, endpoint =>
            endpoint.HttpMethod == "GET" && endpoint.EndpointPath == "/api/route-variant-alias/status");
        Assert.Single(result.DiscoveredEndpoints, endpoint =>
            endpoint.HttpMethod == "GET" && endpoint.EndpointPath == "/api/duplicate-route/status");
    }

    #endregion

    #region Test Helpers

    private string CreateModuleDirectory(string name)
    {
        var moduleDirectory = Path.Combine(_testRootDirectory, name);
        Directory.CreateDirectory(moduleDirectory);
        return moduleDirectory;
    }

    private string CreateModuleDirectoryFromTestOutput()
    {
        var moduleDirectory = CreateModuleDirectory("scanner-module");
        foreach (var sourceFilePath in Directory.GetFiles(AppContext.BaseDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var targetFilePath = Path.Combine(moduleDirectory, Path.GetFileName(sourceFilePath));
            File.Copy(sourceFilePath, targetFilePath);
        }

        return moduleDirectory;
    }

    #endregion
}

[ApiController]
[Route("api/[controller]")]
public class ModuleEndpointScannerSampleController : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll() => Ok();

    [HttpPost("items/[action]")]
    public IActionResult CreateAsync() => Ok();

    [HttpPut("/absolute")]
    public IActionResult Replace() => Ok();

    [HttpDelete("~/root/[action]")]
    public IActionResult RemoveAsync() => Ok();

    [AcceptVerbs("PATCH", "OPTIONS", Route = "multi")]
    public IActionResult UpdateMultiple() => Ok();
}

[ApiController]
public class ModuleEndpointScannerActionOnlyController : ControllerBase
{
    [HttpGet("action-only/[action]")]
    public IActionResult DetailsAsync() => Ok();
}

[ApiController]
[Route("api/route-variants")]
[Route("api/route-variant-alias")]
public class ModuleEndpointScannerMultiRouteController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok();
}

[ApiController]
[Route("api/duplicate-route")]
[Route("api/duplicate-route")]
public class ModuleEndpointScannerDuplicateRouteController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok();
}