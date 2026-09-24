using System.ComponentModel.DataAnnotations;

using Server.Models.Enums;

namespace Server.Models.Dtos;

/// <summary>
/// Represents a data transfer object for a module.
/// </summary>
public class ModuleDto
{
    /// <summary>
    /// The unique identifier of the module.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the module.
    /// </summary>
    public string ModuleName { get; set; } = null!;

    /// <summary>
    /// The description of the module.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The base endpoint path of the module.
    /// </summary>
    public string BaseEndpointPath { get; set; } = null!;

    /// <summary>
    /// The container port of the module.
    /// </summary>
    public int ContainerPort { get; set; }

    /// <summary>
    /// The status of the module.
    /// </summary>
    public ModuleStatus Status { get; set; }

    /// <summary>
    /// The creation time of the module.
    /// </summary>
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
    public string? ContainerId { get; set; }

    /// <summary>
    /// The host path where files are extracted.
    /// </summary>
    public string StoragePath { get; set; } = null!;

    /// <summary>
    /// The list of registered sub-endpoints belonging to this module.
    /// </summary>
    public IEnumerable<ModuleEndpointDto> SubEndpoints { get; set; } = [];
}

/// <summary>
/// Data transfer object representing an individual sub-endpoint of a module.
/// </summary>
public class ModuleEndpointDto
{
    /// <summary>
    /// The unique identifier of the sub-endpoint.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The HTTP method (e.g. GET, POST).
    /// </summary>
    public string HttpMethod { get; set; } = null!;

    /// <summary>
    /// The relative endpoint route.
    /// </summary>
    public string EndpointPath { get; set; } = null!;

    /// <summary>
    /// Timestamp when the endpoint was detected/created (UTC).
    /// </summary>
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
    [Required(ErrorMessage = "Module name is required.")]
    [MaxLength(100, ErrorMessage = "Module name cannot exceed 100 characters.")]
    public string ModuleName { get; set; } = null!;

    /// <summary>
    /// An optional description of the module.
    /// </summary>
    [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    /// <summary>
    /// The unique base endpoint path (e.g. "/api/v1/resource").
    /// </summary>
    [Required(ErrorMessage = "Base endpoint path is required.")]
    [MaxLength(200, ErrorMessage = "Base endpoint path cannot exceed 200 characters.")]
    public string BaseEndpointPath { get; set; } = null!;

    /// <summary>
    /// An optional container port that the module listens to.
    /// </summary>
    /// <remarks>
    /// If not specified, the server will automatically assign an available port.
    /// </remarks>
    [Range(1, 65535, ErrorMessage = "Container port must be between 1 and 65535.")]
    public int? ContainerPort { get; set; }

    /// <summary>
    /// The ZIP archive containing the module's DLLs and binaries.
    /// </summary>
    [Required(ErrorMessage = "Module file is required.")]
    public IFormFile ModuleFile { get; set; } = null!;
}

/// <summary>
/// Data transfer object for updating basic module metadata.
/// </summary>
public class UpdateModuleDto
{
    /// <summary>
    /// The updated name of the module.
    /// </summary>
    [MaxLength(100, ErrorMessage = "Module name cannot exceed 100 characters.")]
    public string? ModuleName { get; set; }

    /// <summary>
    /// The updated description of the module.
    /// </summary>
    [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }
}

/// <summary>
/// Data transfer object for updating the lifecycle status of a module.
/// </summary>
public class UpdateModuleStatusDto
{
    /// <summary>
    /// The new lifecycle status.
    /// </summary>
    [Required(ErrorMessage = "Module status is required.")]
    public ModuleStatus Status { get; set; }
}

/// <summary>
/// Data transfer object for replacing the module's binary files.
/// </summary>
public class UpdateModuleFilesDto
{
    /// <summary>
    /// The new ZIP archive replacing existing files.
    /// </summary>
    [Required(ErrorMessage = "Module file is required.")]
    public IFormFile ModuleFile { get; set; } = null!;
}