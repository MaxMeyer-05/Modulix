using System.IdentityModel.Tokens.Jwt;

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

    #endregion
}