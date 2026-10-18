using System.ComponentModel.DataAnnotations;

namespace Modulix.ReverseProxy.Models;

/// <summary>
/// Represents a transport-agnostic routing definition for a single active module sub-endpoint.
/// </summary>
public class ProxyEndpointDefinition
{
    /// <summary>
    /// The HTTP method allowed for this endpoint (e.g., GET, POST, PUT, DELETE).
    /// </summary>
    /// <value>e.g., GET</value>
    /// <example>GET</example>
    [Required]
    [MaxLength(10)]
    public string HttpMethod { get; set; } = null!;

    /// <summary>
    /// The relative route path of the endpoint within the module container.
    /// </summary>
    /// <value>e.g., /api/v1/reports</value>
    /// <example>/api/v1/reports</example>
    [Required]
    [MaxLength(200)]
    public string EndpointPath { get; set; } = null!;
}
