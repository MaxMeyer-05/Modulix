using Server.Database.Entities;
using Server.Models.Enums;

namespace Server.TestData;

#region Password Input Test Data

/// <summary>
/// Provides reusable valid passwords with varying complexity, lengths, and character sets.
/// </summary>
public class ValidPasswordTestData : TheoryData<string>
{
    public ValidPasswordTestData()
    {
        Add("StandardPassword123!");
        Add("P@$$w0rd_With_Specials#2026");
        Add("VeryLongPasswordPhraseThatSpansMultipleWordsAndCharacters1234567890!");
        Add("Unicode_Päthwörd_Üñîçødé_🔑");
        Add("   PasswordWithLeadingAndTrailingSpaces   ");
    }
}

/// <summary>
/// Provides combinations of correct passwords and incorrect attempt inputs.
/// </summary>
public class MismatchedPasswordTestData : TheoryData<string, string>
{
    public MismatchedPasswordTestData()
    {
        Add("CorrectPassword123!", "WrongPassword123!");
        Add("CaseSensitiveCheck", "casesensitivecheck");
        Add("TrailingSpaceMatters", "TrailingSpaceMatters ");
        Add("SpecialCharacterCheck!", "SpecialCharacterCheck?");
    }
}

/// <summary>
/// Provides malformed and corrupted hash values.
/// </summary>
public class CorruptedHashTestData : TheoryData<string>
{
    public CorruptedHashTestData()
    {
        Add("not_a_valid_base64_hash");
        Add("AQAAAAIAAAAAEA=="); // Truncated base64 payload
        Add(string.Empty);
    }
}

#endregion

#region User Fixture Helper

/// <summary>
/// Provides reusable factory methods for generating user entity fixtures.
/// </summary>
public static class UserTestFixture
{
    public static User CreateTestUser(string username = "testuser", Roles role = Roles.User) =>
        new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            UserEmail = $"{username}@example.com",
            PasswordHash = "fixture-password-hash",
            Role = role,
            AllowedScopes = ["profile.read"]
        };
}

#endregion