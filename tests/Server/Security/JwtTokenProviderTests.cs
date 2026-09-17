using System.IdentityModel.Tokens.Jwt;

using System.Text;
using System.Security.Claims;

using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

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
    public void Constructor_MissingRequiredConfigurationKey_ThrowsInvalidOperationException(string keyToRemove)
    {
        // Arrange
        var configValues = CreateDefaultConfigDictionary();
        configValues.Remove(keyToRemove);
        var configuration = BuildConfiguration(configValues);
        var logger = NullLogger<JwtTokenProvider>.Instance;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => new JwtTokenProvider(configuration, logger));
    }

    [Fact]
    [Trait("Feature", "ConfigurationValidation")]
    public void Constructor_AllConfigurationKeysPresent_InitializesSuccessfully()
    {
        // Arrange
        var configuration = BuildConfiguration(CreateDefaultConfigDictionary());
        var logger = NullLogger<JwtTokenProvider>.Instance;

        // Act
        var provider = new JwtTokenProvider(configuration, logger);

        // Assert
        Assert.NotNull(provider);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("-1")]
    [Trait("Feature", "ConfigurationValidation")]
    public void Constructor_InvalidAccessTokenLifetime_ThrowsInvalidOperationException(string lifetime)
    {
        // Arrange
        var configValues = CreateDefaultConfigDictionary();
        configValues["Jwt:AccessTokenLifetimeMinutes"] = lifetime;
        var configuration = BuildConfiguration(configValues);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new JwtTokenProvider(configuration, NullLogger<JwtTokenProvider>.Instance));
    }

    [Fact]
    [Trait("Feature", "ConfigurationValidation")]
    public void Constructor_SecretKeyShorterThan32Bytes_ThrowsInvalidOperationException()
    {
        // Arrange
        var configValues = CreateDefaultConfigDictionary();
        configValues["Jwt:SecretKey"] = "too-short";
        var configuration = BuildConfiguration(configValues);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new JwtTokenProvider(configuration, NullLogger<JwtTokenProvider>.Instance));
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

    #endregion

    #region Test Helper Methods

    private static Dictionary<string, string?> CreateDefaultConfigDictionary() =>
        new()
        {
            ["Jwt:Issuer"] = ValidIssuer,
            ["Jwt:Audience"] = ValidAudience,
            ["Jwt:AccessTokenLifetimeMinutes"] = ValidLifetimeMinutes.ToString(),
            ["Jwt:SecretKey"] = ValidSecretKey
        };

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static JwtTokenProvider CreateDefaultTokenProvider()
    {
        var configuration = BuildConfiguration(CreateDefaultConfigDictionary());
        return new JwtTokenProvider(configuration, NullLogger<JwtTokenProvider>.Instance);
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
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

    #endregion
}