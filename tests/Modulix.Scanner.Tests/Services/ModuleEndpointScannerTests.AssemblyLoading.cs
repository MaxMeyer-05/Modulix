using System.Reflection;
using System.Reflection.Emit;

using Microsoft.AspNetCore.Mvc;

namespace Modulix.Scanner.Tests.Services;

/// <summary>
/// Assembly loading tests and dynamic assembly helpers for <see cref="Modulix.Scanner.Services.ModuleEndpointScanner"/>.
/// </summary>
public partial class ModuleEndpointScannerTests
{
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

    [Theory]
    [InlineData("Microsoft.AspNetCore.CustomFeature")]
    [InlineData("Microsoft.Extensions.CustomFeature")]
    [Trait("Feature", "AssemblyLoading")]
    public async Task ScanDirectoryAsync_MicrosoftPrefixedLocalDependency_LoadsFromModuleDirectory(string dependencyAssemblyName)
    {
        CreateModuleWithLocalDependency(dependencyAssemblyName);

        var result = await _sut.ScanDirectoryAsync(_testRootDirectory);

        Assert.Equal("ScannerModule.dll", result.EntryAssemblyFileName);
        var endpoint = Assert.Single(result.DiscoveredEndpoints);
        Assert.Equal("GET", endpoint.HttpMethod);
        Assert.Equal("/healthy", endpoint.EndpointPath);
    }

    #endregion

    #region Helper Methods

    private void PrepareModule(string assemblyPath)
    {
        File.Copy(assemblyPath, Path.Combine(_testRootDirectory, "ScannerModule.dll"));
        var runtimeConfigPath = Path.ChangeExtension(typeof(ModuleEndpointScannerTests).Assembly.Location, ".runtimeconfig.json");
        File.Copy(runtimeConfigPath, Path.Combine(_testRootDirectory, "ScannerModule.runtimeconfig.json"));
    }

    private void CreateModuleWithLocalDependency(string dependencyName)
    {
        var dependencyAssembly = new PersistedAssemblyBuilder(new AssemblyName(dependencyName), typeof(object).Assembly);
        var dependencyModule = dependencyAssembly.DefineDynamicModule(dependencyName);
        var dependencyType = dependencyModule.DefineType("CustomFeatureDependency", TypeAttributes.Public).CreateType()!;
        dependencyAssembly.Save(Path.Combine(_testRootDirectory, $"{dependencyName}.dll"));

        var moduleAssembly = new PersistedAssemblyBuilder(new AssemblyName("ScannerModule"), typeof(object).Assembly);
        var module = moduleAssembly.DefineDynamicModule("ScannerModule");
        module.DefineType("FeatureConsumer", TypeAttributes.Public, dependencyType).CreateType();

        var controller = module.DefineType("HealthyController", TypeAttributes.Public, typeof(ControllerBase));
        controller.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(RouteAttribute).GetConstructor([typeof(string)])!, ["healthy"]));

        var action = controller.DefineMethod("Get", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        action.SetCustomAttribute(new CustomAttributeBuilder(typeof(HttpGetAttribute).GetConstructor(Type.EmptyTypes)!, []));
        action.GetILGenerator().Emit(OpCodes.Ret);
        controller.CreateType();

        moduleAssembly.Save(Path.Combine(_testRootDirectory, "ScannerModule.dll"));
        File.WriteAllText(Path.Combine(_testRootDirectory, "ScannerModule.runtimeconfig.json"), "{}");
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
