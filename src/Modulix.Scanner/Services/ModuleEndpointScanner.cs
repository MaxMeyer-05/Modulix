using System.Reflection;
using System.Runtime.Loader;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Modulix.Scanner.Models;
using Modulix.Scanner.Services.Interfaces;

namespace Modulix.Scanner.Services;

/// <inheritdoc cref="IModuleEndpointScanner"/>
public class ModuleEndpointScanner : IModuleEndpointScanner
{
    /// <summary>
    /// A custom assembly load context for loading module assemblies.
    /// </summary>
    private sealed class ModuleLoadContext : AssemblyLoadContext
    {
        /// <summary>
        /// The assembly dependency resolver for the module.
        /// </summary>
        private readonly AssemblyDependencyResolver _resolver;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModuleLoadContext"/> class with the specified entry assembly path.
        /// </summary>
        /// <param name="entryAssemblyPath">The path to the entry assembly of the module.</param>
        public ModuleLoadContext(string entryAssemblyPath)
            : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        }

        /// <summary>
        /// Loads the specified assembly into the context.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly to load.</param>
        /// <returns>The loaded assembly, or null if the assembly could not be loaded.</returns>
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name?.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) == true ||
                assemblyName.Name?.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) == true)
            {
                return null;
            }

            var dependencyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return dependencyPath is null ? null : LoadFromAssemblyPath(dependencyPath);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleEndpointScanner"/> class.
    /// </summary>
    public ModuleEndpointScanner() { }

    /// <inheritdoc/>
    public Task<ModuleScanResultDto> ScanDirectoryAsync(string moduleDirectoryPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!Directory.Exists(moduleDirectoryPath))
            throw new DirectoryNotFoundException($"The specified module directory does not exist: {moduleDirectoryPath}");

        // Find the entry assembly file name within the module directory.
        string? entryAssemblyFileName = FindEntryAssemblyFileName(moduleDirectoryPath, ct);

        var discoveredEndpoints = new List<DiscoveredEndpointDto>();

        var entryAssemblyPath = Path.Combine(moduleDirectoryPath, entryAssemblyFileName);
        var loadContext = new ModuleLoadContext(entryAssemblyPath);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(entryAssemblyPath);

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                var loaderErrors = string.Join(Environment.NewLine, ex.LoaderExceptions
                    .OfType<Exception>()
                    .Select(exception => $"{exception.GetType().Name}: {exception.Message}")
                    .Distinct(StringComparer.Ordinal));

                throw new InvalidOperationException(
                    $"Cannot scan module '{entryAssemblyFileName}' because not all assembly types could be loaded." +
                    Environment.NewLine + loaderErrors,
                    ex);
            }

            // Scan for endpoints
            discoveredEndpoints.AddRange(ScanEndpoint(types, ct));
        }
        finally
        {
            loadContext.Unload();
        }

        ct.ThrowIfCancellationRequested();

        var distinctEndpoints = discoveredEndpoints
            .GroupBy(e => new { e.HttpMethod, e.EndpointPath })
            .Select(g => g.First())
            .OrderBy(e => e.EndpointPath)
            .ThenBy(e => e.HttpMethod)
            .ToList(); 

        var result = new ModuleScanResultDto
        {
            EntryAssemblyFileName = entryAssemblyFileName,
            DiscoveredEndpoints = distinctEndpoints
        };

        return Task.FromResult(result);
    }

    /// <summary>
    /// Finds the entry assembly file name for the module by looking for a corresponding DLL for each runtime config file.
    /// </summary>
    /// <param name="moduleDirectoryPath">The path to the module directory.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The entry assembly file name.</returns>
    /// <exception cref="FileNotFoundException">Thrown if no runtime config files or entry assembly DLL is found.</exception>
    private string FindEntryAssemblyFileName(string moduleDirectoryPath, CancellationToken ct)
    {
        var runtimeConfigFiles = Directory.GetFiles(moduleDirectoryPath, "*.runtimeconfig.json", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".runtimeconfig.dev.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (runtimeConfigFiles.Count == 0)
            throw new FileNotFoundException("No runtime config files found for the module.", moduleDirectoryPath);

        string? entryAssemblyFileName = null;
        foreach (var runtimeConfigFile in runtimeConfigFiles)
        {
            ct.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(runtimeConfigFile);
            var baseName = fileName.Substring(0, fileName.IndexOf(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase));
            var dllFileName = $"{baseName}.dll";

            if (File.Exists(Path.Combine(moduleDirectoryPath, dllFileName)))
            {
                entryAssemblyFileName = dllFileName;
                break;
            }
        }

        if (entryAssemblyFileName == null)
            throw new FileNotFoundException("No entry assembly found for the module.", moduleDirectoryPath);

        return entryAssemblyFileName;
    }

    /// <summary>
    /// Scans the given types for API endpoints and returns a list of discovered endpoints.
    /// </summary>
    /// <param name="types">The array of types to scan.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A list of discovered endpoints.</returns>
    private static List<DiscoveredEndpointDto> ScanEndpoint(Type[] types, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var discoveredEndpoints = new List<DiscoveredEndpointDto>();

        var controllerTypes = types.Where(t => t.IsClass && !t.IsAbstract &&
                (typeof(ControllerBase).IsAssignableFrom(t) || t.GetCustomAttribute<ApiControllerAttribute>() != null));

        foreach (var controllerType in controllerTypes)
        {
            ct.ThrowIfCancellationRequested();

            var baseRouteTemplates = controllerType
                .GetCustomAttributes<RouteAttribute>(inherit: true)
                .Select(route => route.Template ?? string.Empty)
                .DefaultIfEmpty(string.Empty)
                .ToList();
            var methods = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                ct.ThrowIfCancellationRequested();

                var attributes = method.GetCustomAttributes().ToList();
                var httpMethodAttributes = attributes.OfType<IActionHttpMethodProvider>().ToList();
                var routeTemplateProviders = attributes.OfType<IRouteTemplateProvider>().ToList();
                var routeAttributes = routeTemplateProviders
                    .Where(route => route.Template is not null || route.Order is not null || route.Name is not null)
                    .ToList();
                var sharedHttpMethodAttributes = httpMethodAttributes
                    .Where(attribute => attribute is not IRouteTemplateProvider route || !routeAttributes.Contains(route))
                    .ToList();
                var actionRoutes = routeAttributes.Cast<IRouteTemplateProvider?>().ToList();

                if (routeAttributes.Count == 0 ||
                    (routeAttributes.All(route => route is IActionHttpMethodProvider) &&
                     routeTemplateProviders.Count > routeAttributes.Count))
                {
                    actionRoutes.Add(null);
                }

                foreach (var actionRoute in actionRoutes)
                {
                    ct.ThrowIfCancellationRequested();

                    var actionTemplate = actionRoute?.Template ?? string.Empty;
                    IEnumerable<IActionHttpMethodProvider> applicableHttpMethodAttributes =
                        actionRoute is IActionHttpMethodProvider routeHttpMethodProvider
                            ? [routeHttpMethodProvider]
                            : sharedHttpMethodAttributes;
                    var httpMethods = applicableHttpMethodAttributes
                        .SelectMany(attribute => attribute.HttpMethods?.Any() == true ? attribute.HttpMethods : ["GET"])
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var baseRouteTemplate in baseRouteTemplates)
                    {
                        ct.ThrowIfCancellationRequested();

                        var resolvedPath = BuildAndResolveRoute(baseRouteTemplate, actionTemplate, controllerType, method);

                        foreach (var httpMethod in httpMethods)
                        {
                            ct.ThrowIfCancellationRequested();

                            discoveredEndpoints.Add(new DiscoveredEndpointDto
                            {
                                HttpMethod = httpMethod.Trim().ToUpperInvariant(),
                                EndpointPath = resolvedPath
                            });
                        }
                    }
                }
            }
        }

        return discoveredEndpoints;
    }

    /// <summary>
    /// Builds and resolves the full route for a given controller and action method.
    /// </summary>
    /// <param name="baseRoute">The base route template from the controller.</param>
    /// <param name="actionRoute">The route template from the action method.</param>
    /// <param name="controllerType">The type of the controller.</param>
    /// <param name="method">The action method info.</param>
    /// <returns>The fully resolved and normalized route.</returns>
    private static string BuildAndResolveRoute(string baseRoute, string actionRoute, Type controllerType, MethodInfo method)
    {
        if (actionRoute.StartsWith('/') || actionRoute.StartsWith("~/"))
        {
            var absoluteTemplate = actionRoute.TrimStart('~');
            return NormalizePath(ResolveRouteTokens(absoluteTemplate, controllerType, method));
        }

        var resolvedBase = ResolveRouteTokens(baseRoute, controllerType, method).Trim('/');
        var resolvedAction = ResolveRouteTokens(actionRoute, controllerType, method).Trim('/');

        string fullRoute;
        if (string.IsNullOrEmpty(resolvedBase))
            fullRoute = resolvedAction;
        else if (string.IsNullOrEmpty(resolvedAction))
            fullRoute = resolvedBase;
        else
            fullRoute = $"{resolvedBase}/{resolvedAction}";

        return NormalizePath(fullRoute);
    }

    /// <summary>
    /// Resolves route tokens like [controller] and [action] in the given template.
    /// </summary>
    /// <param name="template">The route template containing tokens.</param>
    /// <param name="controllerType">The type of the controller.</param>
    /// <param name="method">The action method info.</param>
    /// <returns>The route template with tokens replaced by actual values.</returns>
    private static string ResolveRouteTokens(string template, Type controllerType, MethodInfo method)
    {
        if (string.IsNullOrWhiteSpace(template))
            return string.Empty;

        var controllerName = controllerType.Name;
        if (controllerName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
        {
            controllerName = controllerName.Substring(0, controllerName.Length - "Controller".Length);
        }

        var actionName = method.Name;
        if (actionName.EndsWith("Async", StringComparison.OrdinalIgnoreCase))
        {
            actionName = actionName.Substring(0, actionName.Length - "Async".Length);
        }

        return template
            .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes the given path by ensuring it starts with a '/' and does not end with a '/'.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The normalized path.</returns>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        var trimmed = path.Trim().TrimEnd('/');
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }
}