using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Modulix.Client.Models.Dtos;

/// <summary>
/// Represents a data transfer object for a module version.
/// </summary>
public class ModuleVersionDto
{
    /// <summary>
    /// The unique identifier of the module version.
    /// </summary>
    /// <value>e.g., 1</value>
    /// <example>1</example>
    public int Id { get; set; }

    /// <summary>
    /// The version string of the module.
    /// </summary>
    /// <value>e.g., "v1.0.0"</value>
    /// <example>v1.0.0</example>
    public string Version { get; set; } = null!;

    /// <summary>
    /// The changelog of the module version.
    /// </summary>
    /// <value>e.g., Initial release with basic analytics features.</value>
    /// <example>Initial release with basic analytics features.</example>
    public string? Changelog { get; set; }

    /// <summary>
    /// The storage path of the module version.
    /// </summary>
    /// <value>e.g., /storage/modules/{moduleId}/versions/v1.0.0</value>
    /// <example>/storage/modules/{moduleId}/versions/v1.0.0</example>
    public string? StoragePath { get; set; }

    /// <summary>
    /// The release date of the module version (UTC).
    /// </summary>
    /// <value>e.g., 2024-06-05T12:34:56Z</value>
    /// <example>2024-06-05T12:34:56Z</example>
    public DateTime ReleasedAtUtc { get; set; }
}

/// <summary>
/// Represents a data transfer object for creating a new module version.
/// </summary>
public class ModuleVersionCreateDto
{
    /// <summary>
    /// The version string of the module.
    /// </summary>
    /// <value>e.g., "v1.0.0"</value>
    /// <example>v1.0.0</example>
    [Required(ErrorMessage = "Version is required.")]
    [MaxLength(50, ErrorMessage = "Version cannot exceed 50 characters.")]
    public string Version { get; set; } = null!;

    /// <summary>
    /// The changelog of the module version.
    /// </summary>
    /// <value>e.g., Initial release with basic analytics features.</value>
    /// <example>Initial release with basic analytics features.</example>
    [MaxLength(500, ErrorMessage = "Changelog cannot exceed 500 characters.")]
    public string? Changelog { get; set; }

    /// <summary>
    /// The ZIP archive containing the module version's DLLs and binaries.
    /// </summary>
    /// <value>e.g., module.zip</value>
    /// <example>module.zip</example>
    [Required(ErrorMessage = "Module file is required.")]
    public IFormFile ModuleFile { get; set; } = null!;
}

/// <summary>
/// Represents a data transfer object for updating an existing module version.
/// </summary>
public class ModuleVersionUpdateDto
{
    /// <summary>
    /// The updated version string of the module.
    /// </summary>
    /// <value>e.g., "v1.0.0"</value>
    /// <example>v1.0.0</example>
    [MaxLength(50, ErrorMessage = "Version cannot exceed 50 characters.")]
    public string? Version { get; set; }

    /// <summary>
    /// The updated changelog of the module version.
    /// </summary>
    /// <value>e.g., Initial release with basic analytics features.</value>
    /// <example>Initial release with basic analytics features.</example>
    [MaxLength(500, ErrorMessage = "Changelog cannot exceed 500 characters.")]
    public string? Changelog { get; set; }
}