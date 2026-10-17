using Modulix.ReverseProxy.Models;

namespace Modulix.ReverseProxy.Services;

/// <summary>
/// Provides methods for dynamically managing module routing and cluster configurations in the YARP reverse proxy.
/// </summary>
public interface IReverseProxyService
{
    /// <summary>
    /// Replaces the entire reverse proxy configuration with the specified running modules and their active endpoints.
    /// </summary>
    /// <param name="modules">The collection of running module routing definitions.</param>
    void UpdateModules(IEnumerable<ProxyModuleDefinition> modules);

    /// <summary>
    /// Adds or updates the reverse proxy configuration for a single running module.
    /// </summary>
    /// <param name="module">The routing definition of the module to add or update.</param>
    void UpsertModule(ProxyModuleDefinition module);

    /// <summary>
    /// Removes the reverse proxy routes and cluster associated with the specified module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module to remove.</param>
    void RemoveModule(Guid moduleId);
}
