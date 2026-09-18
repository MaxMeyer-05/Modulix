namespace Server.Database.Entities;

/// <summary>
/// Represents a refresh token used for obtaining new access tokens.
/// </summary>
public class RefreshToken
{
    /// <summary>
    /// Defines the unique identifier for the refresh token.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Defines the token string for the refresh token.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Defines the user identifier associated with the refresh token.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Defines the expiration date and time (in UTC) of the refresh token.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Indicates whether the refresh token has been revoked.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// Defines the creation date and time (in UTC) of the refresh token.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}