using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

using Server.Security;

namespace Server.TestData;

#region User Id Test Data

/// <summary>
/// Provides reusable valid claim combinations that should resolve to a valid Guid UserId.
/// </summary>
public class ValidUserIdClaimsTestData : TheoryData<string, string, Guid>
{
    public ValidUserIdClaimsTestData()
    {
        var testGuid = Guid.Parse("d3b07384-d113-4672-9c7e-000000000001");

        // JwtRegisteredClaimNames.Sub ("sub")
        Add(JwtRegisteredClaimNames.Sub, testGuid.ToString(), testGuid);

        // ClaimTypes.NameIdentifier
        Add(ClaimTypes.NameIdentifier, testGuid.ToString(), testGuid);
    }
}

/// <summary>
/// Provides invalid or unparseable UserId claim values.
/// </summary>
public class InvalidUserIdClaimsTestData : TheoryData<string, string>
{
    public InvalidUserIdClaimsTestData()
    {
        Add(JwtRegisteredClaimNames.Sub, "invalid-guid-string");
        Add(ClaimTypes.NameIdentifier, string.Empty);
        Add(JwtRegisteredClaimNames.Sub, "12345");
    }
}

#endregion

#region Role Test Data

/// <summary>
/// Provides reusable role claim permutations and casing to verify enum parsing.
/// </summary>
public class ValidRoleClaimsTestData : TheoryData<string, string, Roles>
{
    public ValidRoleClaimsTestData()
    {
        // Primary ClaimTypes.Role with exact enum match
        Add(ClaimTypes.Role, nameof(Roles.Admin), Roles.Admin);

        // Fallback "role" claim with lowercase (testing case-insensitivity)
        Add("role", nameof(Roles.Admin).ToLowerInvariant(), Roles.Admin);

        // Additional enum values
        Add(ClaimTypes.Role, nameof(Roles.User), Roles.User);
    }
}

#endregion

#region Scope Test Data

/// <summary>
/// Provides scope claim string configurations and their expected parsed, distinct list.
/// </summary>
public class ScopeParsingTestData : TheoryData<List<string>, List<string>>
{
    public ScopeParsingTestData()
    {
        // Space-delimited string
        Add(
            new List<string> { "reports.read reports.write" },
            new List<string> { "reports.read", "reports.write" }
        );

        // Multiple scope claims with overlapping items (testing deduplication)
        Add(
            new List<string> { "reports.read", "profile.edit", "reports.read" },
            new List<string> { "reports.read", "profile.edit" }
        );

        // Mixed space-delimited entries with extra spaces
        Add(
            new List<string> { "  admin.access   reports.read  ", "reports.read" },
            new List<string> { "admin.access", "reports.read" }
        );
    }
}

#endregion