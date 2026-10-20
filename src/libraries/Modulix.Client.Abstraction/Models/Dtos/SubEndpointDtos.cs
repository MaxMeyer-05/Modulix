using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

using Modulix.Client.Models.Enums;

namespace Modulix.Client.Models.Dtos;

/// <summary>
/// Data transfer object representing an individual sub-endpoint of a module.
/// </summary>
public class ModuleSubEndpointDto
{
    /// <summary>
    /// The unique identifier of the sub-endpoint.
    /// </summary>
    /// <value>e.g., 3fa85f64-5717-4562-b3fc-2c963f66afa6</value>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public Guid Id { get; set; }

    /// <summary>
    /// The HTTP method (e.g. GET, POST).
    /// </summary>
    /// <value>e.g., GET, POST</value>
    /// <example>GET</example>
    public string HttpMethod { get; set; } = null!;

    /// <summary>
    /// The relative endpoint route.
    /// </summary>
    /// <value>e.g., /api/v1/analytics</value>
    /// <example>/api/v1/analytics</example>
    public string EndpointPath { get; set; } = null!;

    /// <summary>
    /// The status of the sub-endpoint.
    /// </summary>
    /// <value>e.g., Active, Inactive</value>
    /// <example>Active</example>
    public ModuleEndpointsStatus Status { get; set; }

    /// <summary>
    /// The version string in which this sub-endpoint was discovered.
    /// </summary>
    /// <value>e.g., "v1.0.0"</value>
    /// <example>v1.0.0</example>
    public string DiscoveredInVersion { get; set; } = null!;

    /// <summary>
    /// Timestamp when the endpoint was detected/created (UTC).
    /// </summary>
    /// <value>e.g., 2024-06-05T12:34:56Z</value>
    /// <example>2024-06-05T12:34:56Z</example>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Detailed information about a module's sub-endpoint, including the parent module name.
/// </summary>
public class ModuleSubEndpointsDetailDto : ModuleSubEndpointDto
{    
    /// <summary>
    /// The name of the parent module to which this endpoint belongs.
    /// </summary>
    /// <value>e.g., AnalyticsModule</value>
    /// <example>AnalyticsModule</example>
    public string ModuleName { get; set; } = null!;
}

/// <summary>
/// Represents the data required to create a new module endpoint.
/// </summary>
public class CreateModuleSubEndpointDto
{
    /// <summary>
    /// The HTTP method for the new module endpoint (e.g., GET, POST, PUT, DELETE).
    /// </summary>
    /// <value>e.g., GET</value>
    /// <example>GET</example>
    [Required(ErrorMessage = "HTTP method is required.")]
    [MaxLength(10, ErrorMessage = "HTTP method cannot exceed 10 characters.")]
    public string HttpMethod { get; set; } = null!;

    /// <summary>
    /// The base endpoint path for the new module.
    /// </summary>
    /// <value>e.g., /api/v1/analytics</value>
    /// <example>/api/v1/analytics</example>
    [Required(ErrorMessage = "Base endpoint path is required.")]
    [MaxLength(200, ErrorMessage = "Base endpoint path cannot exceed 200 characters.")]
    public string EndpointPath { get; set; } = null!;
}

/// <summary>
/// Represents a report detailing discrepancies between a module's actual endpoints and the expected endpoints.
/// </summary>
public class SubEndpointDiscrepancyReportDto
{
    /// <summary>
    /// The unique identifier of the module for which the endpoint discrepancy report is generated.
    /// </summary>
    /// <value>e.g., 123e4567-e89b-12d3-a456-426614174000</value>
    /// <example>123e4567-e89b-12d3-a456-426614174000</example>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// Indicates whether there is a discrepancy between the module's actual endpoints 
    /// and the expected endpoints.
    /// </summary>
    /// <value>e.g., true</value>
    /// <example>true</example>
    public bool HasDiscrepancy { get; set; }

    /// <summary>
    /// The list of endpoints that match the expected configuration.
    /// </summary>
    public List<DiscoveredSubEndpointDto>? MatchedEndpoints { get; set; }

    /// <summary>
    /// The list of extra endpoints that are present.
    /// </summary>
    public List<DiscoveredSubEndpointDto>? ExtraEndpoints { get; set; }

    /// <summary>
    /// The list of missing endpoints that are expected.
    /// </summary>
    public List<DiscoveredSubEndpointDto>? MissingEndpoints { get; set; }
}

/// <summary>
/// Represents a discovered HTTP route from a scanned module assembly.
/// </summary>
public class DiscoveredSubEndpointDto
{
    /// <summary>
    /// The HTTP method.
    /// </summary>
    /// <value>e.g., GET, POST, PUT, DELETE</value>
    /// <example>GET</example>
    [Required]
    [MaxLength(10)]
    public string HttpMethod { get; set; } = null!;

    /// <summary>
    /// The relative endpoint route path.
    /// </summary>
    /// <value>e.g., /health, /items/{id}</value>
    /// <example>/health</example>
    [Required]
    [MaxLength(200)]
    public string EndpointPath { get; set; } = null!;
}

/// <summary>
/// Payload sent to confirm the final list of endpoints for a module in PendingConfirmation state.
/// </summary>
public class ConfirmEndpointsDto
{
    /// <summary>
    /// The discrepancy report that was generated during the upload/update.
    /// The user confirms these exact missing and extra endpoints.
    /// </summary>
    [Required]
    public List<SubEndpointDiscrepancyReportDto> ConfirmedEndpoints { get; set; } = [];
}