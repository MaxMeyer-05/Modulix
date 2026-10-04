namespace Server.Models.Dtos;

/// <summary>
/// Represents a refresh token.
/// </summary>
public class RefreshTokenDto
{
    /// <summary>
    /// Defines the token value.
    /// </summary>
    /// <value>e.g., "refresh_token_12345"</value>
    /// <example>refresh_token_12345</example>
    public string Token { get; set; } = null!;

    /// <summary>
    /// Defines the user ID associated with the refresh token.
    /// </summary>
    /// <value>e.g., 3fa85f64-5717-4562-b3fc-2c963f66afa6</value>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public Guid UserId { get; set; }

    /// <summary>
    /// Defines the expiration time of the refresh token.
    /// </summary>
    /// <value>e.g., 2024-06-05T12:34:56Z</value>
    /// <example>2024-06-05T12:34:56Z</example>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Indicates whether the refresh token has been revoked.
    /// </summary>
    /// <value>e.g., true, false</value>
    /// <example>false</example>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// Defines the creation time of the refresh token.
    /// </summary>
    /// <value>e.g., 2024-06-05T12:34:56Z</value>
    /// <example>2024-06-05T12:34:56Z</example>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Indicates whether the refresh token is active.
    /// </summary>
    /// <value>e.g., true, false</value>
    /// <example>true</example>
    public bool IsActive => !IsRevoked && ExpiresAtUtc > DateTime.UtcNow;
}