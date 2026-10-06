using System.ComponentModel.DataAnnotations;

using Modulix.Models.Enums;

namespace Modulix.Models.Dtos;

/// <summary>
/// Represents a data transfer object for a module.
/// </summary>
public class ModuleDto
{
    /// <summary>
    /// The unique identifier of the module.
    /// </summary>
    /// <value>e.g., 3fa85f64-5717-4562-b3fc-2c963f66afa6</value>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the module.
    /// </summary>
    /// <value>e.g., AnalyticsModule</value>
    /// <example>AnalyticsModule</example>
    public string ModuleName { get; set; } = null!;

    /// <summary>
    /// The description of the module.
    /// </summary>
    /// <value>e.g., Provides analytics capabilities.</value>
    /// <example>Provides analytics capabilities.</example>
    public string? Description { get; set; }

    /// <summary>
    /// The base endpoint path of the module.
    /// </summary>
    /// <value>e.g., /modules/analytics</value>
    /// <example>/modules/analytics</example>
    public string BaseEndpointPath { get; set; } = null!;

    /// <summary>
    /// The container port of the module.
    /// </summary>
    /// <value>e.g., 8080</value>
    /// <example>8080</example>
    public int ContainerPort { get; set; }

    /// <summary>
    /// The status of the module.
    /// </summary>
    /// <value>e.g., Running, Stopped</value>
    /// <example>Running</example>
    public ModuleStatus Status { get; set; }

    /// <summary>
    /// The creation time of the module.
    /// </summary>
    /// <value>e.g., 2024-06-05T12:34:56Z</value>
    /// <example>2024-06-05T12:34:56Z</example>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Detailed data transfer object of a module including its sub-endpoints and storage path.
/// </summary>
public class ModuleDetailDto : ModuleDto
{
    /// <summary>
    /// The Docker container ID associated with this module.
    /// </summary>
    /// <value>e.g., "container_id_12345"</value>
    /// <example>container_id_12345</example>
    public string? ContainerId { get; set; }

    /// <summary>
    /// The host path where files are extracted.
    /// </summary>
    /// <value>e.g., "/var/lib/modules/analytics"</value>
    /// <example>/var/lib/modules/analytics</example>
    public string StoragePath { get; set; } = null!;

    /// <summary>
    /// The list of registered sub-endpoints belonging to this module.
    /// </summary>
    public IEnumerable<ModuleEndpointDto> Endpoints { get; set; } = [];
}

/// <summary>
/// Data transfer object representing an individual sub-endpoint of a module.
/// </summary>
public class ModuleEndpointDto
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
    /// Timestamp when the endpoint was detected/created (UTC).
    /// </summary>
    /// <value>e.g., 2024-06-05T12:34:56Z</value>
    /// <example>2024-06-05T12:34:56Z</example>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Data transfer object for creating a new module with an initial archive upload.
/// </summary>
public class CreateModuleDto
{
    /// <summary>
    /// The display name of the module.
    /// </summary>
    /// <value>e.g., AnalyticsModule</value>
    /// <example>AnalyticsModule</example>
    [Required(ErrorMessage = "Module name is required.")]
    [MaxLength(100, ErrorMessage = "Module name cannot exceed 100 characters.")]
    public string ModuleName { get; set; } = null!;

    /// <summary>
    /// An optional description of the module.
    /// </summary>
    /// <value>e.g., Provides analytics capabilities.</value>
    /// <example>Provides analytics capabilities.</example>
    [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    /// <summary>
    /// The unique base endpoint path (e.g. "/module/resource").
    /// </summary>
    /// <value>e.g., /module/resource</value>
    /// <example>/module/resource</example>
    [Required(ErrorMessage = "Base endpoint path is required.")]
    [MaxLength(200, ErrorMessage = "Base endpoint path cannot exceed 200 characters.")]
    public string BaseEndpointPath { get; set; } = null!;

    /// <summary>
    /// An optional container port that the module listens to.
    /// </summary>
    /// <remarks>
    /// If not specified, the server will automatically assign an available port.
    /// </remarks>
    /// <value>e.g., 8080</value>
    /// <example>8080</example>
    [Range(1, 65535, ErrorMessage = "Container port must be between 1 and 65535.")]
    public int? ContainerPort { get; set; }

    /// <summary>
    /// The ZIP archive containing the module's DLLs and binaries.
    /// </summary>
    /// <value>e.g., module.zip</value>
    /// <example>module.zip</example>
    [Required(ErrorMessage = "Module file is required.")]
    public IFormFile ModuleFile { get; set; } = null!;

    /// <summary>
    /// The initial endpoints to be created for the module.
    /// </summary>
    public IEnumerable<CreateModuleEndpointDto>? InitialEndpoints { get; set; }
}

/// <summary>
/// Data transfer object for updating basic module metadata.
/// </summary>
public class UpdateModuleDto
{
    /// <summary>
    /// The updated name of the module.
    /// </summary>
    /// <value>e.g., AnalyticsModule</value>
    /// <example>AnalyticsModule</example>
    [MaxLength(100, ErrorMessage = "Module name cannot exceed 100 characters.")]
    public string? ModuleName { get; set; }

    /// <summary>
    /// The updated description of the module.
    /// </summary>
    /// <value>e.g., Provides analytics capabilities.</value>
    /// <example>Provides analytics capabilities.</example>
    [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }
}

/// <summary>
/// Data transfer object for replacing the module's binary files.
/// </summary>
public class UpdateModuleFilesDto
{
    /// <summary>
    /// The new ZIP archive replacing existing files.
    /// </summary>
    /// <value>e.g., module.zip</value>
    /// <example>module.zip</example>
    [Required(ErrorMessage = "Module file is required.")]
    public IFormFile ModuleFile { get; set; } = null!;
}

/// <summary>
/// Represents the data required to create a new module endpoint.
/// </summary>
public class CreateModuleEndpointDto
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
public class EndpointDiscrepancyReportDto
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
    public List<DiscoveredEndpointDto>? MatchedEndpoints { get; set; }

    /// <summary>
    /// The list of extra endpoints that are present.
    /// </summary>
    public List<DiscoveredEndpointDto>? ExtraEndpoints { get; set; }

    /// <summary>
    /// The list of missing endpoints that are expected.
    /// </summary>
    public List<DiscoveredEndpointDto>? MissingEndpoints { get; set; }
}

/// <summary>
/// Result returned after an upload or update operation.
/// </summary>
public class ModuleCreationResultDto
{
    /// <summary>
    /// The details of the created or updated module.
    /// </summary>
    public ModuleDetailDto Module { get; set; } = null!;

    /// <summary>
    /// The report detailing any discrepancies between the module's actual endpoints and the expected endpoints.
    /// </summary>
    public EndpointDiscrepancyReportDto? DiscrepancyReport { get; set; }
}

/// <summary>
/// Payload sent to confirm the final list of endpoints for a module in PendingConfirmation state.
/// </summary>
public class ConfirmEndpointsDto
{
    /// <summary>
    /// The list of endpoints that have been confirmed for the module.
    /// </summary>
    [Required]
    public List<EndpointDiscrepancyReportDto> ConfirmedEndpoints { get; set; } = [];
}