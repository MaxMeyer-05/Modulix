using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Server.Models.Enums;

namespace Server.Database.Entities;

/// <summary>
/// Represents a user entity in the database.
/// </summary>
[Table("users")]
[Index(nameof(UserEmail), IsUnique = true)]
public class User
{
    /// <summary>
    /// Defines the unique identifier for the user.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    /// <summary>
    /// Defines the email of the user.
    /// </summary>
    [Required]
    [EmailAddress]
    public string UserEmail { get; set; } = null!;

    /// <summary>
    /// Defines the hashed password of the user.
    /// </summary>
    [Required]    
    public string PasswordHash { get; set; } = null!;

    /// <summary>
    /// Defines the role of the user.
    /// </summary>
    [Required]
    public Roles Role { get; set; } = Roles.User;

    /// <summary>
    /// Defines the scopes allowed for the user.
    /// </summary>
    public IEnumerable<string>? AllowedScopes { get; set; }
    /// <summary>
    /// Defines whether the user is active.
    /// </summary>
    [Required]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Defines the date and time when the user was created.
    /// </summary>
    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Defines the refresh tokens associated with the user.
    /// </summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}