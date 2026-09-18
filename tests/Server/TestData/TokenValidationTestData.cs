using Server.Models;
using Server.Security;

namespace Server.TestData;

#region Success Scenario Test Data

/// <summary>
/// Provides reusable UserSessionDataDto fixtures representing different user roles and claim setups.
/// </summary>
public class TokenValidationSuccessTestData : TheoryData<UserSessionDataDto>
{
    public TokenValidationSuccessTestData()
    {
        // Scenario 1: Standard user without scopes
        Add(new UserSessionDataDto
        {
            UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Role = Roles.User,
            Scopes = new List<string>(),
            Claims = new Dictionary<string, string>
            {
                ["sub"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                ["role"] = "User"
            }
        });

        // Scenario 2: Administrator with multiple scopes
        Add(new UserSessionDataDto
        {
            UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Role = Roles.Admin,
            Scopes = new List<string> { "reports.read", "orders.write", "admin.access" },
            Claims = new Dictionary<string, string>
            {
                ["sub"] = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                ["role"] = "Administrator",
                ["scope"] = "reports.read orders.write admin.access"
            }
        });

        // Scenario 3: Minimal session data
        Add(new UserSessionDataDto
        {
            UserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Role = Roles.User,
            Scopes = null,
            Claims = null
        });
    }
}

#endregion

#region Failure Scenario Test Data

/// <summary>
/// Provides various error messages representing standard JWT validation failure scenarios.
/// </summary>
public class TokenValidationFailureTestData : TheoryData<string?>
{
    public TokenValidationFailureTestData()
    {
        Add("Token has expired.");
        Add("Invalid signature algorithm.");
        Add("Required claims (UserId or Role) are missing in the token.");
        Add("Token contains an invalid UserId or Role claim.");
        Add(string.Empty);
        Add(null);
    }
}

#endregion