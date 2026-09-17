using System.Text;
using System.IdentityModel.Tokens.Jwt;

using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Server.Models;
using Server.Database.Entities;

namespace Server.Security;

/// <summary>
/// Provides functionality for generating and validating JWT tokens.
/// </summary>
public class JwtTokenProvider : IJwtTokenProvider
{
    private readonly JwtSecurityTokenHandler _jwtTokenHandler;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly TimeProvider _timeProvider;

    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessTokenLifetimeMinutes;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtTokenProvider"/> class.
    /// </summary>
    /// <param name="options">The validated JWT configuration.</param>
    /// <param name="timeProvider">The clock used for token timestamps.</param>
    public JwtTokenProvider(
        IOptions<JwtOptions> options,
        TimeProvider timeProvider)
    {
        _jwtTokenHandler = new JwtSecurityTokenHandler();
        _timeProvider = timeProvider;

        var jwtOptions = options.Value;
        _issuer = jwtOptions.Issuer;
        _audience = jwtOptions.Audience;
        _accessTokenLifetimeMinutes = jwtOptions.AccessTokenLifetimeMinutes;

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey));
    }

    /// <inheritdoc/>
    public TokenResultDto CreateTokenPair(Guid userId, Roles role, IEnumerable<string>? scopes = null)
    {
        var issuedAtUtc = GetCurrentUtcSecond();
        var refreshToken = GenerateRefreshToken(userId, 1, issuedAtUtc);
        var accessToken = GenerateAccessToken(userId, role, scopes, issuedAtUtc);

        return new TokenResultDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            AccessTokenExpiresAtUtc = issuedAtUtc.AddMinutes(_accessTokenLifetimeMinutes),
            RefreshTokenExpiresAtUtc = refreshToken.ExpiresAtUtc
        };
    }

    /// <inheritdoc/>
    public string GenerateAccessToken(Guid userId, Roles role, IEnumerable<string>? scopes = null)
        => GenerateAccessToken(userId, role, scopes, GetCurrentUtcSecond());

    private string GenerateAccessToken(
        Guid userId,
        Roles role,
        IEnumerable<string>? scopes,
        DateTime issuedAtUtc)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.Role, role.ToString())
        };

        if (scopes != null)
        {
            claims.AddRange(scopes
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Select(scope => new Claim("scope", scope.Trim())));
        }

        var expires = issuedAtUtc.AddMinutes(_accessTokenLifetimeMinutes);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            NotBefore = issuedAtUtc,
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
        };

        var token = _jwtTokenHandler.CreateToken(tokenDescriptor);
        return _jwtTokenHandler.WriteToken(token);
    }

    /// <inheritdoc/>
    public RefreshToken GenerateRefreshToken(Guid userId, int daysLifetime = 1)
        => GenerateRefreshToken(userId, daysLifetime, GetCurrentUtcSecond());

    private RefreshToken GenerateRefreshToken(Guid userId, int daysLifetime, DateTime issuedAtUtc)
    {
        if (daysLifetime <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(daysLifetime),
                "Refresh token lifetime must be greater than zero.");
        }

        var randomBytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(randomBytes);
        var expiresAtUtc = issuedAtUtc.AddDays(daysLifetime);

        return new RefreshToken
        {
            Token = token,
            UserId = userId,
            ExpiresAtUtc = expiresAtUtc,
            IsRevoked = false,
            CreatedAtUtc = issuedAtUtc
        };
    }

    private DateTime GetCurrentUtcSecond() =>
        DateTimeOffset.FromUnixTimeSeconds(_timeProvider.GetUtcNow().ToUnixTimeSeconds()).UtcDateTime;
}