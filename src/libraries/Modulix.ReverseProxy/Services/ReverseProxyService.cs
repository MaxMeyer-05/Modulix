using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

using Modulix.ReverseProxy.Models;

namespace Modulix.ReverseProxy.Services;

/// <inheritdoc cref="IReverseProxyService"/>
public class ReverseProxyService : IReverseProxyService, IProxyConfigProvider
{
    /// <summary>
    /// The authorization policy name for anonymous access.
    /// </summary>
    private const string AnonymousAuthorizationPolicy = "Anonymous";

    /// <summary>
    /// The synchronization lock for thread-safe operations.
    /// </summary>
    private readonly Lock _syncRoot = new();

    /// <summary>
    /// The collection of currently active module definitions, keyed by their unique identifiers.
    /// </summary>
    private readonly Dictionary<Guid, ProxyModuleDefinition> _modules = [];

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<ReverseProxyService> _logger;

    /// <summary>
    /// The in-memory proxy configuration instance.
    /// </summary>
    private volatile InMemoryProxyConfig _config = new([], []);

    /// <summary>
    /// Initializes a new instance of the <see cref="ReverseProxyService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public ReverseProxyService(ILogger<ReverseProxyService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public IProxyConfig GetConfig() => _config;

    /// <inheritdoc/>
    public void UpdateModules(IEnumerable<ProxyModuleDefinition> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        lock (_syncRoot)
        {
            _modules.Clear();
            foreach (var module in modules)
            {
                ValidateModuleDefinition(module);
                _modules[module.ModuleId] = module;
            }

            RebuildConfigurationLocked();
            _logger.LogInformation("Updated reverse proxy configuration with {ModuleCount} module(s).", _modules.Count);
        }
    }

    /// <inheritdoc/>
    public void UpsertModule(ProxyModuleDefinition module)
    {
        ValidateModuleDefinition(module);

        lock (_syncRoot)
        {
            _modules[module.ModuleId] = module;
            RebuildConfigurationLocked();
            _logger.LogInformation(
                "Upserted reverse proxy configuration for module '{ModuleId}' at '/{BaseUrlPath}' (Port: {ContainerPort}, Endpoints: {EndpointCount}).",
                module.ModuleId,
                NormalizeBasePath(module.BaseUrlPath),
                module.ContainerPort,
                module.Endpoints?.Count ?? 0);
        }
    }

    /// <inheritdoc/>
    public void RemoveModule(Guid moduleId)
    {
        lock (_syncRoot)
        {
            if (!_modules.Remove(moduleId))
                return;

            RebuildConfigurationLocked();
            _logger.LogInformation("Removed reverse proxy configuration for module '{ModuleId}'.", moduleId);
        }
    }

    /// <summary>
    /// Rebuilds the in-memory YARP routes and clusters from the current module definitions and signals a change.
    /// Must be called while holding <see cref="_syncRoot"/>.
    /// </summary>
    private void RebuildConfigurationLocked()
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>(_modules.Count);

        foreach (var module in _modules.Values)
        {
            var clusterId = $"module-cluster-{module.ModuleId:N}";
            var normalizedBasePath = NormalizeBasePath(module.BaseUrlPath);
            var prefix = $"/{normalizedBasePath}";
            var destinationAddress = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{module.ContainerPort}");

            clusters.Add(new ClusterConfig
            {
                ClusterId = clusterId,
                Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    ["container"] = new DestinationConfig
                    {
                        Address = destinationAddress
                    }
                }
            });

            var endpoints = module.Endpoints ?? [];
            for (var i = 0; i < endpoints.Count; i++)
            {
                var endpoint = endpoints[i];
                if (string.IsNullOrWhiteSpace(endpoint.HttpMethod) || string.IsNullOrWhiteSpace(endpoint.EndpointPath))
                    continue;

                var relativePath = endpoint.EndpointPath.Trim().TrimStart('/');
                var matchPath = string.IsNullOrEmpty(relativePath)
                    ? prefix
                    : $"{prefix}/{relativePath}";

                var route = new RouteConfig
                {
                    RouteId = $"module-route-{module.ModuleId:N}-{i}",
                    ClusterId = clusterId,
                    AuthorizationPolicy = AnonymousAuthorizationPolicy,
                    Match = new RouteMatch
                    {
                        Path = matchPath,
                        Methods = [endpoint.HttpMethod.Trim().ToUpperInvariant()]
                    }
                }.WithTransformPathRemovePrefix(prefix);

                routes.Add(route);
            }
        }

        var previousConfig = _config;
        _config = new InMemoryProxyConfig(routes, clusters);
        previousConfig.SignalChange();
    }

    /// <summary>
    /// Validates a module routing definition before applying it to the proxy configuration.
    /// </summary>
    /// <param name="module">The module definition to validate.</param>
    private static void ValidateModuleDefinition(ProxyModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (module.ModuleId == Guid.Empty)
            throw new ArgumentException("Module ID cannot be empty.", nameof(module));

        if (string.IsNullOrWhiteSpace(NormalizeBasePath(module.BaseUrlPath)))
            throw new ArgumentException("Module base URL path cannot be empty.", nameof(module));

        if (module.ContainerPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(module), module.ContainerPort, "Container port must be between 1 and 65535.");
    }

    /// <summary>
    /// Normalizes a module base URL path by trimming whitespace and leading/trailing slashes.
    /// </summary>
    /// <param name="baseUrlPath">The raw base URL path.</param>
    /// <returns>The normalized base URL path without leading or trailing slashes.</returns>
    private static string NormalizeBasePath(string baseUrlPath)
        => baseUrlPath?.Trim().Trim('/') ?? string.Empty;

    /// <summary>
    /// Represents an immutable in-memory snapshot of YARP routes and clusters with a change token.
    /// </summary>
    private sealed class InMemoryProxyConfig : IProxyConfig
    {
        /// <summary>
        /// The cancellation token.
        /// </summary>
        private readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="InMemoryProxyConfig"/> class with the specified routes and clusters.
        /// </summary>
        /// <param name="routes">The collection of route configurations.</param>
        /// <param name="clusters">The collection of cluster configurations.</param>
        public InMemoryProxyConfig(
            IReadOnlyList<RouteConfig> routes, 
            IReadOnlyList<ClusterConfig> clusters)
        {
            Routes = routes;
            Clusters = clusters;
            ChangeToken = new CancellationChangeToken(_cts.Token);
        }

        /// <summary>
        /// Gets the collection of route configurations.
        /// </summary>
        public IReadOnlyList<RouteConfig> Routes { get; }

        /// <summary>
        /// Gets the collection of cluster configurations.
        /// </summary>
        public IReadOnlyList<ClusterConfig> Clusters { get; }

        /// <summary>
        /// Gets the change token that signals when the configuration has changed.
        /// </summary>
        public IChangeToken ChangeToken { get; }

        /// <summary>
        /// Signals that the configuration has changed by cancelling the internal token.
        /// </summary>
        public void SignalChange() => _cts.Cancel();
    }
}
