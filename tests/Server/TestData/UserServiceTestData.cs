using Server.Models.Dtos;
using Server.Models.Enums;

namespace Server.TestData;

#region Password Update Test Data

/// <summary>
/// Provides mismatched password combinations for password update tests.
/// </summary>
public class MismatchedUpdatePasswordTestData : TheoryData<UpdatePasswordDto>
{
    public MismatchedUpdatePasswordTestData()
    {
        Add(new UpdatePasswordDto
        {
            CurrentPassword = "OldPassword123!",
            NewPassword = "NewPassword123!",
            ConfirmNewPassword = "DifferentPassword456!"
        });

        Add(new UpdatePasswordDto
        {
            CurrentPassword = "OldPassword123!",
            NewPassword = "CaseSensitive123!",
            ConfirmNewPassword = "casesensitive123!"
        });

        Add(new UpdatePasswordDto
        {
            CurrentPassword = "OldPassword123!",
            NewPassword = "TrailingSpace123!",
            ConfirmNewPassword = "TrailingSpace123! "
        });
    }
}

#endregion

#region Role Update Test Data

/// <summary>
/// Provides role transition combinations from an existing role to a new target role.
/// </summary>
public class UserRoleUpdateTestData : TheoryData<Roles, Roles>
{
    public UserRoleUpdateTestData()
    {
        Add(Roles.User, Roles.Admin);
        Add(Roles.Admin, Roles.User);
    }
}

#endregion

#region Scope Update Test Data

/// <summary>
/// Provides distinct scope collection updates for a user.
/// </summary>
public class UserScopesUpdateTestData : TheoryData<List<string>>
{
    public UserScopesUpdateTestData()
    {
        Add(["profile.read", "profile.write"]);
        Add(["reports.read", "orders.write", "admin.access"]);
        Add([]);
    }
}

#endregion