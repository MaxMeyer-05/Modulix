using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Modulix.Database.Entities;

/// <summary>
/// Represents the metadata of a reusable Dockerfile template stored on disk.
/// </summary>
[Table("dockerfile_templates")]
[Index(nameof(StoragePath), IsUnique = true)]
public class DockerfileTemplate
{
    /// <summary>
    /// The unique identifier of the template.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// The display name of the template, independent of its file name.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string FileName { get; set; } = null!;

    /// <summary>
    /// The description of the template.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// The template file path relative to the configured storage base path.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string StoragePath { get; set; } = null!;

    /// <summary>
    /// The creation time of the template in UTC.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The last update time of the template in UTC.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The modules associated with this Dockerfile template.
    /// </summary>
    public ICollection<Module>? Modules { get; set; } = [];
}
