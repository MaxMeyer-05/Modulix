namespace Server.Models.Dtos;

/// <summary>
/// Represents the result of a token operation.
/// </summary>
public class TokenResultDto
{
    /// <summary>
    /// Defines the access token.
    /// </summary>
    /// <value>e.g., "access_token_12345"</value>
    /// <example>access_token_12345</example>
    public string AccessToken { get; set; } = null!;

    /// <summary>
    /// Defines the refresh token.
    /// </summary>
    /// <value>e.g., "refresh_token_12345"</value>
    /// <example>refresh_token_12345</example>
    public string RefreshToken { get; set; } = null!;

    /// <summary>
    /// Defines the expiration time of the access token.
    /// </summary>
    /// <value>e.g., 2024-06-05T12:34:56Z</value>
    /// <example>2024-06-05T12:34:56Z</example>
    public DateTime AccessTokenExpiresAtUtc { get; set; }

    /// <summary>
    /// Defines the expiration time of the refresh token.
    /// </summary>
    /// <value>e.g., 2024-06-05T12:34:56Z</value>
    /// <example>2024-06-05T12:34:56Z</example>
    public DateTime RefreshTokenExpiresAtUtc { get; set; }
}