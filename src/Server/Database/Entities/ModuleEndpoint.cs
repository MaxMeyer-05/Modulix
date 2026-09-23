using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Database.Entities;

/// <summary>
/// Represents an endpoint within a module.
/// </summary>
[Table("module_endpoints")]
[Index(nameof(ModuleId), nameof(HttpMethod), nameof(EndpointPath), IsUnique = true)]
public class ModuleEndpoint
{
    /// <summary>
    /// The unique identifier of the module endpoint.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    /// <summary>
    /// The unique identifier of the module to which this endpoint belongs.
    /// </summary>
    [Required]
    public Guid ModuleId { get; set; }

    /// <summary>
    /// The HTTP method of the endpoint (e.g., GET, POST).
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string HttpMethod { get; set; } = null!;

    /// <summary>
    /// The path of the endpoint within the module.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string EndpointPath { get; set; } = null!;

    /// <summary>
    /// The creation time of the module endpoint.
    /// </summary>
    [Required]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The module to which this endpoint belongs.
    /// </summary>
    [Required]
    [ForeignKey(nameof(ModuleId))]
    public Module Module { get; set; } = null!;
}