namespace Server.Models;

/// <summary>
/// Represents the result of a token operation.
/// </summary>
public class TokenResultDto
{
    /// <summary>
    /// Defines the access token.
    /// </summary>
    public string AccessToken { get; set; } = null!;

    /// <summary>
    /// Defines the refresh token.
    /// </summary>
    public string RefreshToken { get; set; } = null!;

    /// <summary>
    /// Defines the expiration time of the access token.
    /// </summary>
    public DateTime AccessTokenExpiresAtUtc { get; set; }

    /// <summary>
    /// Defines the expiration time of the refresh token.
    /// </summary>
    public DateTime RefreshTokenExpiresAtUtc { get; set; }
}