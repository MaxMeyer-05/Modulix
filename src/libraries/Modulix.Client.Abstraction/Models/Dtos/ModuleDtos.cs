using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

using Modulix.Client.Models.Enums;

namespace Modulix.Client.Models.Dtos;

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
    /// The base URL path of the module.
    /// </summary>
    /// <value>e.g., /module/analytics</value>
    /// <example>/module/analytics</example>
    public string BaseUrlPath { get; set; } = null!;

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
    /// The current version of the module.
    /// </summary>
    /// <value>e.g., "v1.0.0"</value>
    /// <example>v1.0.0</example>
    public string CurrentVersion { get; set; } = null!;

    /// <summary>
    /// The list of registered sub-endpoints belonging to this module.
    /// </summary>
    public IEnumerable<ModuleSubEndpointDto> Endpoints { get; set; } = [];

    /// <summary>
    /// The list of versions associated with this module.
    /// </summary>
    public IEnumerable<ModuleVersionDto> Versions { get; set; } = [];
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
    /// The unique base URL path (e.g. "/module/resource").
    /// </summary>
    /// <value>e.g., /module/resource</value>
    /// <example>/module/resource</example>
    [Required(ErrorMessage = "Base URL path is required.")]
    [MaxLength(200, ErrorMessage = "Base URL path cannot exceed 200 characters.")]
    public string BaseUrlPath { get; set; } = null!;

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
    /// The initial version string of the module.
    /// </summary>
    /// <value>e.g., "v1.0.0"</value>
    /// <example>v1.0.0</example>
    [Required(ErrorMessage = "Version is required.")]
    [MaxLength(50, ErrorMessage = "Version cannot exceed 50 characters.")]
    public string Version { get; set; } = null!;

    /// <summary>
    /// An optional changelog for the initial version of the module.
    /// </summary>
    /// <value>e.g., Initial release with basic analytics features.</value>
    /// <example>Initial release with basic analytics features.</example>
    [MaxLength(500, ErrorMessage = "Changelog cannot exceed 500 characters.")]
    public string? Changelog { get; set; }

    /// <summary>
    /// The initial endpoints to be created for the module.
    /// </summary>
    public IEnumerable<CreateModuleSubEndpointDto>? InitialEndpoints { get; set; }
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
    public SubEndpointDiscrepancyReportDto? DiscrepancyReport { get; set; }

    /// <summary>
    /// An optional message providing additional information about the result of the operation.
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Data transfer object representing the statistics of a module.
/// </summary>
public class ModuleStatsDto
{
    /// <summary>
    /// The CPU usage of the module.
    /// </summary>
    public double CpuUsage { get; set; }

    /// <summary>
    /// The memory usage of the module.
    /// </summary>
    public double MemoryUsage { get; set; }

    /// <summary>
    /// The uptime of the module in seconds.
    /// </summary>
    public double Uptime { get; set; }

    /// <summary>
    /// The exit code of the module's last run.
    /// </summary>
    public int ExitCode { get; set; }
}