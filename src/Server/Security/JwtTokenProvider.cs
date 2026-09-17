using System.Text;
using System.IdentityModel.Tokens.Jwt;

using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.IdentityModel.Tokens;

using Server.Models;
using Server.Database.Entities;

namespace Server.Security;

/// <summary>
/// Provides functionality for generating and validating JWT tokens.
/// </summary>
public class JwtTokenProvider : IJwtTokenProvider
{
    private readonly IConfiguration _configuration;
    private readonly JwtSecurityTokenHandler _jwtTokenHandler;
    private readonly SymmetricSecurityKey _signingKey;

    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessTokenLifetimeMinutes;

    private readonly ILogger<JwtTokenProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtTokenProvider"/> class.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="logger">The logger instance.</param>
    public JwtTokenProvider(
        IConfiguration configuration, 
        ILogger<JwtTokenProvider> logger)
    {
        _configuration = configuration;
        _jwtTokenHandler = new JwtSecurityTokenHandler();

        _issuer = _configuration["Jwt:Issuer"] 
            ?? throw new InvalidOperationException("JWT issuer is not configured.");
        _audience = _configuration["Jwt:Audience"] 
            ?? throw new InvalidOperationException("JWT audience is not configured.");
        _accessTokenLifetimeMinutes = int.Parse(_configuration["Jwt:AccessTokenLifetimeMinutes"] 
            ?? throw new InvalidOperationException("JWT access token lifetime is not configured."));

        var secretKey = _configuration["Jwt:SecretKey"] 
            ?? throw new InvalidOperationException("JWT secret key is not configured.");
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        _logger = logger;
    }

    /// <inheritdoc/>
    public TokenResultDto CreateTokenPair(Guid userId, Roles role, IEnumerable<string>? scopes = null)
    {
        var refreshToken = GenerateRefreshToken(userId);
        var accessToken = GenerateAccessToken(userId, role, scopes);
        var accessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(_accessTokenLifetimeMinutes);

        return new TokenResultDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            AccessTokenExpiresAtUtc = accessTokenExpiresAtUtc,
            RefreshTokenExpiresAtUtc = refreshToken.ExpiresAtUtc
        };
    }

    /// <inheritdoc/>
    public string GenerateAccessToken(Guid userId, Roles role, IEnumerable<string>? scopes = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.Role, role.ToString())
        };

        if (scopes != null)
        {
            claims.AddRange(scopes.Select(scope => new Claim("scope", scope.Trim())));
        }

        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_accessTokenLifetimeMinutes);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            NotBefore = now,
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
        };

        var token = _jwtTokenHandler.CreateToken(tokenDescriptor);
        return _jwtTokenHandler.WriteToken(token);
    }

    /// <inheritdoc/>
    public RefreshToken GenerateRefreshToken(Guid userId, int daysLifetime = 1)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(randomBytes);
        var expiresAtUtc = DateTime.UtcNow.AddDays(daysLifetime);

        return new RefreshToken
        {
            Token = token,
            UserId = userId,
            ExpiresAtUtc = expiresAtUtc,
            IsRevoked = false,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}