using Server.Models;
using Server.Database.Entities;

namespace Server.Security;

/// <summary>
/// Provides an interface for generating and validating JWT tokens.
/// </summary>
public interface IJwtTokenProvider
{
    /// <summary>
    /// Creates an access token and a refresh token for the specified user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="role">The user's role.</param>
    /// <param name="scopes">The optional scopes to include in the access token.</param>
    /// <returns>The token pair and its expiration times.</returns>
    TokenResultDto CreateTokenPair(Guid userId, Roles role, IEnumerable<string>? scopes);

    /// <summary>
    /// Generates an access token for the specified user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="role">The user's role.</param>
    /// <param name="scopes">The optional scopes to include in the access token.</param>
    /// <param name="issuedAtUtc">The UTC time when the access token is issued.</param>
    /// <returns>The generated access token.</returns>
    string GenerateAccessToken(Guid userId, Roles role, IEnumerable<string>? scopes, DateTime issuedAtUtc);

    /// <summary>
    /// Generates a refresh token for the specified user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="daysLifetime">The refresh token lifetime in days.</param>
    /// <param name="issuedAtUtc">The UTC time when the refresh token is issued.</param>
    /// <returns>The generated refresh token entity.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the specified refresh token lifetime is less than or equal to zero.
    /// </exception>
    RefreshToken GenerateRefreshToken(Guid userId, int daysLifetime, DateTime issuedAtUtc);
}