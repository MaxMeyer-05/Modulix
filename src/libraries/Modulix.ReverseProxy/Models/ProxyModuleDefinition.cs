using System.ComponentModel.DataAnnotations;

namespace Modulix.ReverseProxy.Models;

/// <summary>
/// Represents a transport-agnostic routing definition for a running module and its exposed endpoints.
/// </summary>
public class ProxyModuleDefinition
{
    /// <summary>
    /// The unique identifier of the module.
    /// </summary>
    /// <value>e.g., 3fa85f64-5717-4562-b3fc-2c963f66afa6</value>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    [Required]
    public Guid ModuleId { get; set; }

    /// <summary>
    /// The unique base URL path prefix for the module (e.g., "analytics" or "/analytics").
    /// </summary>
    /// <value>e.g., analytics</value>
    /// <example>analytics</example>
    [Required]
    [MaxLength(200)]
    public string BaseUrlPath { get; set; } = null!;

    /// <summary>
    /// The local container port to which requests for this module are forwarded.
    /// </summary>
    /// <value>e.g., 8080</value>
    /// <example>8080</example>
    [Range(1, 65535)]
    public int ContainerPort { get; set; }

    /// <summary>
    /// The active sub-endpoints exposed by the module through the reverse proxy.
    /// </summary>
    public IReadOnlyList<ProxyEndpointDefinition> Endpoints { get; set; } = [];
}
