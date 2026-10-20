using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Modulix.Client.Models.Enums;

namespace Modulix.Database.Entities;

/// <summary>
/// Represents a module within the system.
/// </summary>
[Table("modules")]
[Index(nameof(BaseUrlPath), IsUnique = true)]
[Index(nameof(ContainerPort), IsUnique = true)]
public class Module
{
    /// <summary>
    /// The unique identifier of the module.
    /// </summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The name of the module.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ModuleName { get; set; } = null!;

    /// <summary>
    /// The description of the module.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// The base URL path of the module.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string BaseUrlPath { get; set; } = null!;

    /// <summary>
    /// The container ID of the module.
    /// </summary>
    [ConcurrencyCheck]
    public string? ContainerId { get; set; }

    /// <summary>
    /// The internal TCP port of the module, not its dynamically assigned loopback host port.
    /// </summary>
    [Required]
    public int ContainerPort { get; set; }

    /// <summary>
    /// The current version of the module.
    /// </summary>
    [Required]
    [MaxLength(50)]
    [ConcurrencyCheck]
    public string CurrentVersion { get; set; } = null!;

    /// <summary>
    /// The status of the module.
    /// </summary>
    [Required]
    public ModuleStatus Status { get; set; } = ModuleStatus.Created;

    /// <summary>
    /// The creation time of the module.
    /// </summary>
    [Required]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The sub-endpoints of the module.
    /// </summary>
    public ICollection<ModuleEndpoint> SubEndpoints { get; set; } = [];

    /// <summary>
    /// The versions of the module.
    /// </summary>
    public ICollection<ModuleVersion> Versions { get; set; } = [];
}