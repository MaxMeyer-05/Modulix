using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Modulix.Database.Entities;

/// <summary>
/// Represents a version of a module.
/// </summary>
[Table("module_versions")]
[Index(nameof(StoragePath), IsUnique = true)]
public class ModuleVersion
{
    /// <summary>
    /// The unique identifier for the module version.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// The version string of the module.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Version { get; set; } = null!;

    /// <summary>
    /// The changelog for this version of the module.
    /// </summary>
    [MaxLength(500)]
    public string? Changelog { get; set; }

    /// <summary>
    /// The storage path for this version of the module.
    /// </summary>
    [Required]
    [MaxLength(200)]
    [ConcurrencyCheck]
    public string StoragePath { get; set; } = null!;

    /// <summary>
    /// The release date of this version of the module.
    /// </summary>
    [Required]
    public DateTime ReleaseDate { get; set; } = DateTime.UtcNow;
}