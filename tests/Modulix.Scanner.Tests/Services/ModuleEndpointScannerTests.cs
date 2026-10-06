using System.Reflection;
using System.Reflection.Emit;

using Microsoft.AspNetCore.Mvc;

using Modulix.Scanner.Services;
using Modulix.Scanner.Tests.TestData;

namespace Modulix.Scanner.Tests.Services;

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
        _testRootDirectory = Path.Combine(Path.GetTempPath(), "Modulix_Scanner_Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRootDirectory);
        _sut = new ModuleEndpointScanner();
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Directory.Delete(_testRootDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region ScanDirectory Validation Tests

    [Theory]
    [ClassData(typeof(InvalidModuleDirectoryPathsTestData))]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_InvalidDirectoryPath_ThrowsDirectoryNotFoundException(string? invalidPath)
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            _sut.ScanDirectoryAsync(invalidPath!));
    }

    [Fact]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_DirectoryDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        var missingDirectory = Path.Combine(_testRootDirectory, "missing");

        var exception = await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            _sut.ScanDirectoryAsync(missingDirectory));

        Assert.Contains(missingDirectory, exception.Message);
    }

    [Fact]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_NoRuntimeConfig_ThrowsFileNotFoundException()
    {
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _sut.ScanDirectoryAsync(_testRootDirectory));

        Assert.Equal("No runtime config files found for the module.", exception.Message);
        Assert.Equal(_testRootDirectory, exception.FileName);
    }

    [Fact]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_EntryAssemblyDoesNotExist_ThrowsFileNotFoundException()
    {
        File.WriteAllText(Path.Combine(_testRootDirectory, "Missing.runtimeconfig.json"), "{}");

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _sut.ScanDirectoryAsync(_testRootDirectory));

        Assert.Equal("No entry assembly found for the module.", exception.Message);
        Assert.Equal(_testRootDirectory, exception.FileName);
    }

    [Fact]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_OnlyDevelopmentRuntimeConfig_ThrowsFileNotFoundException()
    {
        PrepareModule(typeof(ModuleEndpointScanner).Assembly.Location);
        File.Move(
            Path.Combine(_testRootDirectory, "ScannerModule.runtimeconfig.json"),
            Path.Combine(_testRootDirectory, "ScannerModule.runtimeconfig.dev.json"));

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _sut.ScanDirectoryAsync(_testRootDirectory));

        Assert.Equal("No runtime config files found for the module.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_ModuleOnlyInSubdirectory_ThrowsFileNotFoundException()
    {
        PrepareModule(typeof(ModuleEndpointScanner).Assembly.Location);
        var nestedDirectory = Path.Combine(_testRootDirectory, "nested");
        Directory.CreateDirectory(nestedDirectory);

        foreach (var filePath in Directory.GetFiles(_testRootDirectory))
            File.Move(filePath, Path.Combine(nestedDirectory, Path.GetFileName(filePath)));

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _sut.ScanDirectoryAsync(_testRootDirectory));

        Assert.Equal("No runtime config files found for the module.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_UnmatchedRuntimeConfig_SkipsItAndFindsEntryAssembly()
    {
        File.WriteAllText(Path.Combine(_testRootDirectory, "Missing.runtimeconfig.json"), "{}");
        PrepareModule(typeof(ModuleEndpointScanner).Assembly.Location);

        var result = await _sut.ScanDirectoryAsync(_testRootDirectory);

        Assert.Equal("ScannerModule.dll", result.EntryAssemblyFileName);
        Assert.Empty(result.DiscoveredEndpoints);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a managed assembly")]
    [Trait("Feature", "ScanDirectory")]
    public async Task ScanDirectoryAsync_InvalidAssembly_ThrowsBadImageFormatException(string assemblyContents)
    {
        File.WriteAllText(Path.Combine(_testRootDirectory, "Invalid.runtimeconfig.json"), "{}");
        File.WriteAllText(Path.Combine(_testRootDirectory, "Invalid.dll"), assemblyContents);

        await Assert.ThrowsAsync<BadImageFormatException>(() =>
            _sut.ScanDirectoryAsync(_testRootDirectory));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Feature", "Cancellation")]
    public async Task ScanDirectoryAsync_CancellationRequested_ThrowsBeforeDirectoryValidation(bool directoryExists)
    {
        var moduleDirectory = directoryExists ? _testRootDirectory : Path.Combine(_testRootDirectory, "missing");
        var cancellationToken = new CancellationToken(canceled: true);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _sut.ScanDirectoryAsync(moduleDirectory, cancellationToken));

        Assert.Equal(cancellationToken, exception.CancellationToken);
    }

    #endregion

    #region Endpoint Discovery Tests

    [Theory]
    [ClassData(typeof(ControllerEndpointTestData))]
    [Trait("Feature", "EndpointDiscovery")]
    public async Task ScanDirectoryAsync_ControllerRoutes_ReturnsExpectedEndpoints(string routePrefix, string[] expectedEndpoints)
    {
        PrepareModule(typeof(ModuleEndpointScannerTests).Assembly.Location);

        var result = await _sut.ScanDirectoryAsync(_testRootDirectory);

        var actualEndpoints = result.DiscoveredEndpoints
            .Where(endpoint => endpoint.EndpointPath == routePrefix ||
                endpoint.EndpointPath.StartsWith(routePrefix + "/", StringComparison.Ordinal))
            .Select(endpoint => $"{endpoint.HttpMethod} {endpoint.EndpointPath}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedEndpoints.Order(StringComparer.Ordinal), actualEndpoints);
    }

    [Fact]
    [Trait("Feature", "EndpointDiscovery")]
    public async Task ScanDirectoryAsync_AllControllers_ReturnsDistinctSortedEndpointsAndEntryAssembly()
    {
        PrepareModule(typeof(ModuleEndpointScannerTests).Assembly.Location);

        var result = await _sut.ScanDirectoryAsync(_testRootDirectory);

        var expectedEndpoints = new ControllerEndpointTestData()
            .SelectMany(testCase => (string[])testCase[1])
            .Order(StringComparer.Ordinal);
        var actualEndpoints = result.DiscoveredEndpoints
            .Select(endpoint => $"{endpoint.HttpMethod} {endpoint.EndpointPath}")
            .Order(StringComparer.Ordinal);

        Assert.Equal("ScannerModule.dll", result.EntryAssemblyFileName);
        Assert.Equal(expectedEndpoints, actualEndpoints);
        Assert.Equal(
            result.DiscoveredEndpoints.OrderBy(endpoint => endpoint.EndpointPath).ThenBy(endpoint => endpoint.HttpMethod),
            result.DiscoveredEndpoints);
    }

    [Fact]
    [Trait("Feature", "EndpointDiscovery")]
    public async Task ScanDirectoryAsync_NoControllers_ReturnsEmptyEndpointList()
    {
        PrepareModule(typeof(ModuleEndpointScanner).Assembly.Location);

        var result = await _sut.ScanDirectoryAsync(_testRootDirectory);

        Assert.Equal("ScannerModule.dll", result.EntryAssemblyFileName);
        Assert.Empty(result.DiscoveredEndpoints);
    }

    [Fact]
    [Trait("Feature", "EndpointDiscovery")]
    public async Task ScanDirectoryAsync_RepeatedScans_ReturnsSameEndpoints()
    {
        PrepareModule(typeof(ModuleEndpointScannerTests).Assembly.Location);

        var firstResult = await _sut.ScanDirectoryAsync(_testRootDirectory);
        var secondResult = await _sut.ScanDirectoryAsync(_testRootDirectory);

        Assert.Equal(firstResult.EntryAssemblyFileName, secondResult.EntryAssemblyFileName);
        Assert.NotEmpty(firstResult.DiscoveredEndpoints);
        Assert.Equal(
            firstResult.DiscoveredEndpoints.Select(endpoint => (endpoint.HttpMethod, endpoint.EndpointPath)),
            secondResult.DiscoveredEndpoints.Select(endpoint => (endpoint.HttpMethod, endpoint.EndpointPath)));
    }

    #endregion

    #region Assembly Loading Tests

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Feature", "AssemblyLoading")]
    public async Task ScanDirectoryAsync_MissingDependency_ThrowsInsteadOfReturningPartialEndpoints(bool includeHealthyController)
    {
        CreateModuleWithMissingDependency(includeHealthyController);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ScanDirectoryAsync(_testRootDirectory));

        Assert.Contains("ScannerModule.dll", exception.Message);
        Assert.Contains("not all assembly types could be loaded", exception.Message);
        Assert.Contains("MissingDependency_", exception.Message);

        var reflectionException = Assert.IsType<ReflectionTypeLoadException>(exception.InnerException);
        var loaderExceptions = reflectionException.LoaderExceptions.OfType<Exception>().ToArray();
        Assert.NotEmpty(loaderExceptions);

        foreach (var loaderException in loaderExceptions)
            Assert.Contains($"{loaderException.GetType().Name}: {loaderException.Message}", exception.Message);

        Assert.Equal(includeHealthyController, reflectionException.Types.Any(type => type?.Name == "HealthyController"));
    }

    #endregion

    #region Helper Methods

    private void PrepareModule(string assemblyPath)
    {
        File.Copy(assemblyPath, Path.Combine(_testRootDirectory, "ScannerModule.dll"));
        var runtimeConfigPath = Path.ChangeExtension(typeof(ModuleEndpointScannerTests).Assembly.Location, ".runtimeconfig.json");
        File.Copy(runtimeConfigPath, Path.Combine(_testRootDirectory, "ScannerModule.runtimeconfig.json"));
    }

    private void CreateModuleWithMissingDependency(bool includeHealthyController)
    {
        var dependencyName = "MissingDependency_" + Guid.NewGuid().ToString("N");
        var dependencyAssembly = new PersistedAssemblyBuilder(new AssemblyName(dependencyName), typeof(object).Assembly);
        var dependencyModule = dependencyAssembly.DefineDynamicModule(dependencyName);
        var dependencyType = dependencyModule.DefineType("MissingDependencyBase", TypeAttributes.Public).CreateType()!;

        var moduleAssembly = new PersistedAssemblyBuilder(new AssemblyName("ScannerModule"), typeof(object).Assembly);
        var module = moduleAssembly.DefineDynamicModule("ScannerModule");
        module.DefineType("UnavailableController", TypeAttributes.Public, dependencyType).CreateType();

        if (includeHealthyController)
        {
            var controller = module.DefineType("HealthyController", TypeAttributes.Public, typeof(ControllerBase));
            controller.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(RouteAttribute).GetConstructor([typeof(string)])!, ["healthy"]));

            var action = controller.DefineMethod("Get", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
            action.SetCustomAttribute(new CustomAttributeBuilder(typeof(HttpGetAttribute).GetConstructor(Type.EmptyTypes)!, []));
            action.GetILGenerator().Emit(OpCodes.Ret);
            controller.CreateType();
        }

        moduleAssembly.Save(Path.Combine(_testRootDirectory, "ScannerModule.dll"));
        File.WriteAllText(Path.Combine(_testRootDirectory, "ScannerModule.runtimeconfig.json"), "{}");
    }

    #endregion
}