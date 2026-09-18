using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Database.Entities;

/// <summary>
/// Represents a refresh token used for obtaining new access tokens.
/// </summary>
[Table("refresh_tokens")]
[Index(nameof(Token), IsUnique = true)]
public class RefreshToken
{
    /// <summary>
    /// Defines the unique identifier for the refresh token.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// Defines the token string for the refresh token.
    /// </summary>
    [Required]
    public string Token { get; set; } = null!;

    /// <summary>
    /// Defines the user identifier associated with the refresh token.
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// Defines the expiration date and time (in UTC) of the refresh token.
    /// </summary>
    [Required]
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Indicates whether the refresh token has been revoked.
    /// </summary>
    [Required]
    public bool IsRevoked { get; set; } = false;

    /// <summary>
    /// Defines the creation date and time (in UTC) of the refresh token.
    /// </summary>
    [Required]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Defines the user entity associated with the refresh token.
    /// </summary>
    [Required]
    [ForeignKey("UserId")]
    public User User { get; set; } = null!;
}