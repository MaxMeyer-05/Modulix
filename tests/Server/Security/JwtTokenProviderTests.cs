using System.IdentityModel.Tokens.Jwt;

using System.Text;
using System.Security.Claims;

using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

using Server.TestData;

namespace Server.Security;

/// <summary>
/// Unit tests for <see cref="JwtTokenProvider"/>.
/// </summary>
[Trait("Category", "Security")]
[Trait("SubCategory", "JwtTokenProvider")]
public class JwtTokenProviderTests
{
    private const string ValidSecretKey = "super_secret_key_with_at_least_32_characters_long!";
    private const string ValidIssuer = "ModulixTestIssuer";
    private const string ValidAudience = "ModulixTestAudience";
    private const int ValidLifetimeMinutes = 15;

    #region Constructor & Configuration Tests

    [Theory]
    [ClassData(typeof(MissingJwtConfigurationTestData))]
    [Trait("Feature", "ConfigurationValidation")]
    public void JwtOptionsValidator_MissingRequiredConfigurationKey_ReturnsFailure(string keyToRemove)
    {
        // Arrange
        var options = CreateDefaultJwtOptions();
        SetOptionValue(options, keyToRemove, string.Empty);

        // Act & Assert
        Assert.False(new JwtOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    [Trait("Feature", "ConfigurationValidation")]
    public void Constructor_AllConfigurationKeysPresent_InitializesSuccessfully()
    {
        // Arrange
        // Act
        var provider = CreateDefaultTokenProvider();

        // Assert
        Assert.NotNull(provider);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [Trait("Feature", "ConfigurationValidation")]
    public void JwtOptionsValidator_InvalidAccessTokenLifetime_ReturnsFailure(int lifetime)
    {
        // Arrange
        var options = CreateDefaultJwtOptions();
        options.AccessTokenLifetimeMinutes = lifetime;

        // Act & Assert
        Assert.False(new JwtOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    [Trait("Feature", "ConfigurationValidation")]
    public void JwtOptionsValidator_SecretKeyShorterThan32Bytes_ReturnsFailure()
    {
        // Arrange
        var options = CreateDefaultJwtOptions();
        options.SecretKey = "too-short";

        // Act & Assert
        Assert.False(new JwtOptionsValidator().Validate(null, options).Succeeded);
    }

    #endregion

    #region Access Token Generation Tests

    [Theory]
    [ClassData(typeof(TokenGenerationScenariosTestData))]
    [Trait("Feature", "AccessTokenGeneration")]
    public void GenerateAccessToken_ValidParameters_ProducesTokenWithExpectedClaimsAndMetadata(
        Guid userId, 
        Roles role, 
        List<string>? scopes)
    {
        // Arrange
        var provider = CreateDefaultTokenProvider();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var tokenString = provider.GenerateAccessToken(userId, role, scopes);
        var jwtToken = handler.ReadJwtToken(tokenString);

        // Assert - Structural integrity
        Assert.False(string.IsNullOrWhiteSpace(tokenString));
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwtToken.Header.Alg);
        Assert.Equal(ValidIssuer, jwtToken.Issuer);
        Assert.Contains(ValidAudience, jwtToken.Audiences);

        // Assert - Subject & Role Claims
        Assert.Equal(userId.ToString(), jwtToken.Subject);
        Assert.Contains(jwtToken.Claims, c => c.Type == "role" && c.Value == role.ToString());

        // Assert - Scope Claims
        var tokenScopes = jwtToken.Claims.Where(c => c.Type == "scope").Select(c => c.Value).ToList();
        if (scopes == null || scopes.Count == 0)
        {
            Assert.Empty(tokenScopes);
        }
        else
        {
            Assert.Equal(scopes.Count, tokenScopes.Count);
            Assert.Equal(scopes.OrderBy(s => s), tokenScopes.OrderBy(s => s));
        }

        // Assert - Expiration Window
        var expectedExpiry = DateTime.UtcNow.AddMinutes(ValidLifetimeMinutes);
        Assert.True(jwtToken.ValidTo <= expectedExpiry.AddSeconds(5));
        Assert.True(jwtToken.ValidTo >= expectedExpiry.AddSeconds(-5));
    }

    [Fact]
    [Trait("Feature", "AccessTokenGeneration")]
    public void GenerateAccessToken_WhitespaceAndEmptyScopes_ExcludesEmptyScopes()
    {
        // Arrange
        var provider = CreateDefaultTokenProvider();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var tokenString = provider.GenerateAccessToken(
            Guid.NewGuid(),
            Roles.User,
            ["reports.read", " ", string.Empty, " orders.write "]);
        var tokenScopes = handler.ReadJwtToken(tokenString).Claims
            .Where(claim => claim.Type == "scope")
            .Select(claim => claim.Value)
            .ToList();

        // Assert
        Assert.Equal(["reports.read", "orders.write"], tokenScopes);
    }

    [Fact]
    [Trait("Feature", "AccessTokenValidation")]
    public void GenerateAccessToken_ValidToken_PassesSignatureAndMetadataValidation()
    {
        // Arrange
        var provider = CreateDefaultTokenProvider();
        var userId = Guid.NewGuid();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var tokenString = provider.GenerateAccessToken(userId, Roles.Admin);
        var principal = handler.ValidateToken(tokenString, CreateTokenValidationParameters(), out _);

        // Assert
        var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Assert.Equal(userId.ToString(), subject);
    }

    [Fact]
    [Trait("Feature", "AccessTokenValidation")]
    public void GenerateAccessToken_TamperedToken_ThrowsSecurityTokenException()
    {
        // Arrange
        var provider = CreateDefaultTokenProvider();
        var tokenString = provider.GenerateAccessToken(Guid.NewGuid(), Roles.User);
        var signatureStart = tokenString.LastIndexOf('.') + 1;
        var replacement = tokenString[signatureStart] == 'a' ? 'b' : 'a';
        var tamperedToken = tokenString[..signatureStart] + replacement + tokenString[(signatureStart + 1)..];
        var handler = new JwtSecurityTokenHandler();

        // Act & Assert
        Assert.ThrowsAny<SecurityTokenException>(() =>
            handler.ValidateToken(tamperedToken, CreateTokenValidationParameters(), out _));
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [Trait("Feature", "AccessTokenValidation")]
    public void GenerateAccessToken_InvalidIssuerOrAudience_ThrowsSecurityTokenException(string invalidSetting)
    {
        // Arrange
        var provider = CreateDefaultTokenProvider();
        var tokenString = provider.GenerateAccessToken(Guid.NewGuid(), Roles.User);
        var validationParameters = CreateTokenValidationParameters();
        if (invalidSetting == "issuer")
        {
            validationParameters.ValidIssuer = "incorrect-issuer";
        }
        else
        {
            validationParameters.ValidAudience = "incorrect-audience";
        }

        var handler = new JwtSecurityTokenHandler();

        // Act & Assert
        Assert.ThrowsAny<SecurityTokenException>(() =>
            handler.ValidateToken(tokenString, validationParameters, out _));
    }

    [Fact]
    [Trait("Feature", "AccessTokenValidation")]
    public void CreateTokenValidationParameters_HmacSha384Token_ThrowsSecurityTokenException()
    {
        // Arrange
        var options = CreateDefaultJwtOptions();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())]),
            Expires = DateTime.UtcNow.AddMinutes(1),
            Issuer = options.Issuer,
            Audience = options.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SecretKey)),
                SecurityAlgorithms.HmacSha384)
        };
        var handler = new JwtSecurityTokenHandler();
        var tokenString = handler.WriteToken(handler.CreateToken(tokenDescriptor));

        // Act & Assert
        Assert.ThrowsAny<SecurityTokenException>(() =>
            handler.ValidateToken(tokenString, options.CreateTokenValidationParameters(), out _));
    }

    #endregion

    #region Refresh Token Generation Tests

    [Theory]
    [ClassData(typeof(RefreshTokenLifetimeTestData))]
    [Trait("Feature", "RefreshTokenGeneration")]
    public void GenerateRefreshToken_GivenLifetime_ReturnsCryptographicallyStrongToken(int daysLifetime)
    {
        // Arrange
        var provider = CreateDefaultTokenProvider();
        var userId = Guid.NewGuid();
        var utcBefore = DateTime.UtcNow;

        // Act
        var refreshToken = provider.GenerateRefreshToken(userId, daysLifetime);

        // Assert - Values & Status
        Assert.NotNull(refreshToken);
        Assert.Equal(userId, refreshToken.UserId);
        Assert.False(refreshToken.IsRevoked);
        Assert.False(string.IsNullOrWhiteSpace(refreshToken.Token));

        // Assert - Cryptographic Length (64 bytes Base64-encoded is ~88 chars)
        var tokenBytes = Convert.FromBase64String(refreshToken.Token);
        Assert.Equal(64, tokenBytes.Length);

        // Assert - Expiration Calculation
        var expectedExpiry = utcBefore.AddDays(daysLifetime);
        Assert.True(refreshToken.ExpiresAtUtc >= expectedExpiry.AddSeconds(-5));
        Assert.True(refreshToken.ExpiresAtUtc <= expectedExpiry.AddSeconds(5));
    }

    [Fact]
    [Trait("Feature", "RefreshTokenGeneration")]
    public void GenerateRefreshToken_DefaultLifetime_DefaultsToOneDay()
    {
        // Arrange
        var provider = CreateDefaultTokenProvider();
        var userId = Guid.NewGuid();
        var utcBefore = DateTime.UtcNow;

        // Act
        var refreshToken = provider.GenerateRefreshToken(userId);

        // Assert
        var expectedExpiry = utcBefore.AddDays(1);
        Assert.True(refreshToken.ExpiresAtUtc >= expectedExpiry.AddSeconds(-5));
        Assert.True(refreshToken.ExpiresAtUtc <= expectedExpiry.AddSeconds(5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [Trait("Feature", "RefreshTokenGeneration")]
    public void GenerateRefreshToken_NonPositiveLifetime_ThrowsArgumentOutOfRangeException(int daysLifetime)
    {
        // Arrange
        var provider = CreateDefaultTokenProvider();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            provider.GenerateRefreshToken(Guid.NewGuid(), daysLifetime));
    }

    #endregion

    #region Token Pair Generation Tests

    [Fact]
    [Trait("Feature", "TokenPairGeneration")]
    public void CreateTokenPair_ValidInputs_GeneratesPopulatedTokenResultDto()
    {
        // Arrange
        var provider = CreateDefaultTokenProvider();
        var userId = Guid.NewGuid();
        var role = Roles.Admin;
        var scopes = new List<string> { "audit.read" };
        var utcBefore = DateTime.UtcNow;

        // Act
        var tokenPair = provider.CreateTokenPair(userId, role, scopes);

        // Assert
        Assert.NotNull(tokenPair);
        Assert.False(string.IsNullOrWhiteSpace(tokenPair.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokenPair.RefreshToken));

        // Expirations properly populated
        Assert.True(tokenPair.AccessTokenExpiresAtUtc > utcBefore);
        Assert.True(tokenPair.RefreshTokenExpiresAtUtc > tokenPair.AccessTokenExpiresAtUtc);
    }

    [Fact]
    [Trait("Feature", "TokenPairGeneration")]
    public void CreateTokenPair_UsesTheAccessTokenExpiryForTheResult()
    {
        // Arrange
        var issuedAtUtc = new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
        var provider = CreateDefaultTokenProvider(new FixedTimeProvider(issuedAtUtc));
        var handler = new JwtSecurityTokenHandler();

        // Act
        var tokenPair = provider.CreateTokenPair(Guid.NewGuid(), Roles.User);
        var accessToken = handler.ReadJwtToken(tokenPair.AccessToken);

        // Assert
        Assert.Equal(accessToken.ValidTo, tokenPair.AccessTokenExpiresAtUtc);
        Assert.Equal(issuedAtUtc.UtcDateTime.AddDays(1), tokenPair.RefreshTokenExpiresAtUtc);
    }

    #endregion

    #region Test Helper Methods

    private static JwtOptions CreateDefaultJwtOptions() =>
        new()
        {
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            AccessTokenLifetimeMinutes = ValidLifetimeMinutes,
            SecretKey = ValidSecretKey
        };

    private static void SetOptionValue(JwtOptions options, string key, string value)
    {
        switch (key)
        {
            case "Jwt:Issuer":
                options.Issuer = value;
                break;
            case "Jwt:Audience":
                options.Audience = value;
                break;
            case "Jwt:AccessTokenLifetimeMinutes":
                options.AccessTokenLifetimeMinutes = 0;
                break;
            case "Jwt:SecretKey":
                options.SecretKey = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(key));
        }
    }

    private static JwtTokenProvider CreateDefaultTokenProvider(TimeProvider? timeProvider = null)
    {
        return new JwtTokenProvider(
            Options.Create(CreateDefaultJwtOptions()),
            timeProvider ?? TimeProvider.System);
    }

    private static TokenValidationParameters CreateTokenValidationParameters() =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = ValidIssuer,
            ValidateAudience = true,
            ValidAudience = ValidAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSecretKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    #endregion
}