using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Server.Models.Enums;

namespace Server.Database.Entities;

/// <summary>
/// Represents a module within the system.
/// </summary>
[Table("modules")]
[Index(nameof(BaseEndpointPath), IsUnique = true)]
[Index(nameof(StoragePath), IsUnique = true)]
[Index(nameof(ContainerPort), IsUnique = true)]
public class Module
{
    /// <summary>
    /// The unique identifier of the module.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

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
    /// The base endpoint path of the module.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string BaseEndpointPath { get; set; } = null!;

    /// <summary>
    /// The container ID of the module.
    /// </summary>
    public string? ContainerId { get; set; }

    /// <summary>
    /// The container port of the module.
    /// </summary>
    [Required]
    public int ContainerPort { get; set; }

    /// <summary>
    /// The storage path of the module.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string StoragePath { get; set; } = null!;

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
}