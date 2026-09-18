using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

using Server.Models;
using Server.TestData;

namespace Server.Security;

/// <summary>
/// Unit tests for <see cref="ClaimsPrincipalExtensions.PopulateSessionData"/>.
/// </summary>
[Trait("Category", "Security")]
[Trait("SubCategory", "ClaimsPrincipalExtensions")]
public class ClaimsPrincipalExtensionsTests
{
    #region UserId Extraction Tests

    [Theory]
    [ClassData(typeof(ValidUserIdClaimsTestData))]
    [Trait("Feature", "UserIdExtraction")]
    public void PopulateSessionData_ValidSubOrNameIdentifierClaim_SetsUserIdCorrectly(
        string claimType, 
        string claimValue, 
        Guid expectedUserId)
    {
        // Arrange
        var claims = new List<Claim> { new(claimType, claimValue) };
        var principal = CreatePrincipal(claims);
        var session = new UserSessionDataDto();

        // Act
        principal.PopulateSessionData(session);

        // Assert
        Assert.Equal(expectedUserId, session.UserId);
    }

    [Theory]
    [ClassData(typeof(InvalidUserIdClaimsTestData))]
    [Trait("Feature", "UserIdExtraction")]
    public void PopulateSessionData_InvalidSubClaim_LeavesUserIdDefault(string claimType, string claimValue)
    {
        // Arrange
        var claims = new List<Claim> { new(claimType, claimValue) };
        var principal = CreatePrincipal(claims);
        var session = new UserSessionDataDto();

        // Act
        principal.PopulateSessionData(session);

        // Assert
        Assert.Equal(Guid.Empty, session.UserId);
    }

    [Fact]
    [Trait("Feature", "UserIdExtraction")]
    public void PopulateSessionData_SubAndNameIdentifierBothPresent_PrefersSubClaim()
    {
        // Arrange
        var expectedId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var fallbackId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, expectedId.ToString()),
            new(ClaimTypes.NameIdentifier, fallbackId.ToString())
        };
        var principal = CreatePrincipal(claims);
        var session = new UserSessionDataDto();

        // Act
        principal.PopulateSessionData(session);

        // Assert
        Assert.Equal(expectedId, session.UserId);
    }

    [Theory]
    [InlineData(null, nameof(Roles.Admin))]
    [InlineData("not-a-guid", nameof(Roles.Admin))]
    [InlineData("11111111-1111-1111-1111-111111111111", null)]
    [InlineData("11111111-1111-1111-1111-111111111111", "99")]
    [Trait("Feature", "SessionClaimValidation")]
    public void HasValidSessionClaims_MissingOrInvalidSubjectOrRole_ReturnsFalse(string? subject, string? role)
    {
        // Arrange
        var claims = new List<Claim>();
        if (subject != null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, subject));
        }

        if (role != null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var principal = CreatePrincipal(claims);

        // Act & Assert
        Assert.False(principal.HasValidSessionClaims());
    }

    [Fact]
    [Trait("Feature", "SessionClaimValidation")]
    public void HasValidSessionClaims_ValidSubjectAndRole_ReturnsTrue()
    {
        // Arrange
        var principal = CreatePrincipal(
        [
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, nameof(Roles.User))
        ]);

        // Act & Assert
        Assert.True(principal.HasValidSessionClaims());
    }

    #endregion

    #region Role Extraction Tests

    [Theory]
    [ClassData(typeof(ValidRoleClaimsTestData))]
    [Trait("Feature", "RoleExtraction")]
    public void PopulateSessionData_ValidRoleClaim_ParsesRoleCaseInsensitively(
        string claimType, 
        string claimValue, 
        Roles expectedRole)
    {
        // Arrange
        var claims = new List<Claim> { new(claimType, claimValue) };
        var principal = CreatePrincipal(claims);
        var session = new UserSessionDataDto();

        // Act
        principal.PopulateSessionData(session);

        // Assert
        Assert.Equal(expectedRole, session.Role);
    }

    [Fact]
    [Trait("Feature", "RoleExtraction")]
    public void PopulateSessionData_InvalidRoleClaim_LeavesRoleDefault()
    {
        // Arrange
        var claims = new List<Claim> { new(ClaimTypes.Role, "NonExistentRole123") };
        var principal = CreatePrincipal(claims);
        var session = new UserSessionDataDto();

        // Act
        principal.PopulateSessionData(session);

        // Assert
        Assert.Equal(default, session.Role);
    }

    [Fact]
    [Trait("Feature", "RoleExtraction")]
    public void PopulateSessionData_UndefinedNumericRoleClaim_LeavesRoleDefault()
    {
        // Arrange
        var claims = new List<Claim> { new(ClaimTypes.Role, "99") };
        var principal = CreatePrincipal(claims);
        var session = new UserSessionDataDto();

        // Act
        principal.PopulateSessionData(session);

        // Assert
        Assert.Equal(default, session.Role);
    }

    [Fact]
    [Trait("Feature", "RoleExtraction")]
    public void PopulateSessionData_ClaimTypesRoleAndRoleClaimBothPresent_PrefersClaimTypesRole()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, nameof(Roles.Admin)),
            new("role", nameof(Roles.User))
        };
        var principal = CreatePrincipal(claims);
        var session = new UserSessionDataDto();

        // Act
        principal.PopulateSessionData(session);

        // Assert
        Assert.Equal(Roles.Admin, session.Role);
    }

    #endregion

    #region Scopes Extraction Tests

    [Theory]
    [ClassData(typeof(ScopeParsingTestData))]
    [Trait("Feature", "ScopeExtraction")]
    public void PopulateSessionData_ScopeClaimsSupplied_ParsesDistinctScopes(
        List<string> rawScopeClaims, 
        List<string> expectedScopes)
    {
        // Arrange
        var claims = rawScopeClaims.Select(s => new Claim("scope", s)).ToList();
        var principal = CreatePrincipal(claims);
        var session = new UserSessionDataDto();

        // Act
        principal.PopulateSessionData(session);

        // Assert
        Assert.NotNull(session.Scopes);
        Assert.Equal(expectedScopes.Count, session.Scopes.Count);
        Assert.Equal(expectedScopes.OrderBy(s => s), session.Scopes.OrderBy(s => s));
    }

    [Fact]
    [Trait("Feature", "ScopeExtraction")]
    public void PopulateSessionData_NoScopesSupplied_AssignsEmptyList()
    {
        // Arrange
        var principal = CreatePrincipal([]);
        var session = new UserSessionDataDto();

        // Act
        principal.PopulateSessionData(session);

        // Assert
        Assert.NotNull(session.Scopes);
        Assert.Empty(session.Scopes);
    }

    #endregion

    #region Claims Dictionary Mapping Tests

    [Fact]
    [Trait("Feature", "ClaimsDictionary")]
    public void PopulateSessionData_MultipleClaimsSameType_JoinsWithCommaDelimiter()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("permission", "read"),
            new("permission", "write"),
            new("custom_tag", "alpha")
        };
        var principal = CreatePrincipal(claims);
        var session = new UserSessionDataDto();

        // Act
        principal.PopulateSessionData(session);

        // Assert
        Assert.NotNull(session.Claims);
        Assert.Equal("read, write", session.Claims["permission"]);
        Assert.Equal("alpha", session.Claims["custom_tag"]);
    }

    [Fact]
    [Trait("Feature", "ClaimsDictionary")]
    public void PopulateSessionData_EmptyPrincipal_InitializesEmptyClaimsDictionary()
    {
        // Arrange
        var principal = CreatePrincipal([]);
        var session = new UserSessionDataDto();

        // Act
        principal.PopulateSessionData(session);

        // Assert
        Assert.NotNull(session.Claims);
        Assert.Empty(session.Claims);
    }

    #endregion

    #region Test Helper Methods

    private static ClaimsPrincipal CreatePrincipal(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    #endregion
}