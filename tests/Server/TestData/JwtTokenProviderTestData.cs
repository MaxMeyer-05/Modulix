using Server.Models.Enums;

namespace Server.TestData;

#region Token Generation Inputs

/// <summary>
/// Provides variations of user IDs, roles, and scope collections for token generation tests.
/// </summary>
public class TokenGenerationScenariosTestData : TheoryData<Guid, Roles, List<string>?>
{
    public TokenGenerationScenariosTestData()
    {
        // Standard user without custom scopes
        Add(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Roles.User,
            null
        );

        // Administrator with single scope
        Add(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Roles.Admin,
            new List<string> { "reports.read" }
        );

        // Administrator with multiple scopes
        Add(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Roles.Admin,
            new List<string> { "reports.read", "orders.write", "system.manage" }
        );
    }
}

#endregion

#region Refresh Token Lifetime Inputs

/// <summary>
/// Provides different refresh token lifetime inputs in minutes.
/// </summary>
public class RefreshTokenLifetimeTestData : TheoryData<int>
{
    public RefreshTokenLifetimeTestData()
    {
        Add(1);
        Add(60);
        Add(24 * 60);
    }
}

#endregion

#region Configuration Failure Inputs

/// <summary>
/// Provides missing or invalid configuration keys required by the JwtTokenProvider constructor.
/// </summary>
public class MissingJwtConfigurationTestData : TheoryData<string>
{
    public MissingJwtConfigurationTestData()
    {
        Add("Jwt:Issuer");
        Add("Jwt:Audience");
        Add("Jwt:AccessTokenLifetimeMinutes");
        Add("Jwt:RefreshTokenLifetimeMinutes");
        Add("Jwt:SecretKey");
    }
}

#endregion