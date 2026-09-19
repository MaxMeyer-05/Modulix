using Server.Models.Dtos;
using Server.Models.Enums;

namespace Server.TestData;

#region Registration Test Data

/// <summary>
/// Provides valid registration request combinations.
/// </summary>
public class ValidRegistrationTestData : TheoryData<RegisterDto>
{
    public ValidRegistrationTestData()
    {
        Add(new RegisterDto
        {
            UserEmail = "newuser@example.com",
            UserPassword = "SecurePassword123!",
            Confirm_UserPassword = "SecurePassword123!"
        });

        Add(new RegisterDto
        {
            UserEmail = "admin@example.com",
            UserPassword = "AdminPassword456!",
            Confirm_UserPassword = "AdminPassword456!"
        });
    }
}

/// <summary>
/// Provides registration requests where the password and confirm password fields differ.
/// </summary>
public class MismatchedRegistrationPasswordsTestData : TheoryData<string, string>
{
    public MismatchedRegistrationPasswordsTestData()
    {
        Add("Password123!", "DifferentPassword123!");
        Add("Password123!", "password123!");
        Add("Password123!", "Password123! ");
        Add("Password123!", string.Empty);
    }
}

#endregion

#region Login Test Data

/// <summary>
/// Provides combinations of non-matching passwords and attempts for invalid login tests.
/// </summary>
public class InvalidLoginCredentialsTestData : TheoryData<string, string>
{
    public InvalidLoginCredentialsTestData()
    {
        Add("registered@example.com", "WrongPassword!");
        Add("registered@example.com", "wrongpassword");
        Add("registered@example.com", string.Empty);
    }
}

#endregion