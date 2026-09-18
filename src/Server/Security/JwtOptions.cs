using System.Text;

using Microsoft.IdentityModel.Tokens;

namespace Server.Security;

/// <summary>
/// Defines the configuration values used to issue and validate JWTs.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// Gets or sets the token issuer.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the token audience.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the signing key.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the access-token lifetime in minutes.
    /// </summary>
    public int AccessTokenLifetimeMinutes { get; set; }

    /// <summary>
    /// Creates validation parameters matching the configured token issuer.
    /// </summary>
    public TokenValidationParameters CreateTokenValidationParameters() =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
}