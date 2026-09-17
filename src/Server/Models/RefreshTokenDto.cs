namespace Server.Models;

/// <summary>
/// Represents a refresh token.
/// </summary>
public class RefreshTokenDto
{
    /// <summary>
    /// Defines the token value.
    /// </summary>
    public string Token { get; set; } = null!;

    /// <summary>
    /// Defines the user ID associated with the refresh token.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Defines the expiration time of the refresh token.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Indicates whether the refresh token has been revoked.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// Defines the creation time of the refresh token.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Indicates whether the refresh token is active.
    /// </summary>
    public bool IsActive => !IsRevoked && ExpiresAtUtc > DateTime.UtcNow;
}