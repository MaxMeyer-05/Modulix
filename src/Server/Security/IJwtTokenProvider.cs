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
    TokenResultDto CreateTokenPair(Guid userId, Roles role, IEnumerable<string>? scopes = null);

    /// <summary>
    /// Generates an access token for the specified user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="role">The user's role.</param>
    /// <param name="scopes">The optional scopes to include in the access token.</param>
    /// <returns>The generated access token.</returns>
    string GenerateAccessToken(Guid userId, Roles role, IEnumerable<string>? scopes = null);

    /// <summary>
    /// Generates a refresh token for the specified user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="daysLifetime">The refresh token lifetime in days.</param>
    /// <returns>The generated refresh token entity.</returns>
    RefreshToken GenerateRefreshToken(Guid userId, int daysLifetime = 1);

    /// <summary>
    /// Validates an access token, optionally checking its lifetime.
    /// </summary>
    /// <param name="token">The access token to validate.</param>
    /// <param name="validateLifetime">Whether to validate the token's lifetime.</param>
    /// <returns>The token validation result.</returns>
    TokenValidationResult ValidateAccessToken(string token, bool validateLifetime = true);

    /// <summary>
    /// Renews a token pair using an expired access token and a stored refresh token.
    /// </summary>
    /// <param name="expiredAccessToken">The expired access token.</param>
    /// <param name="storedRefreshToken">The stored refresh token used for renewal.</param>
    /// <returns>The renewed token pair and its expiration times.</returns>
    TokenResultDto RenewTokens(string expiredAccessToken, RefreshToken storedRefreshToken);
}