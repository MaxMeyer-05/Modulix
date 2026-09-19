using Server.Models.Enums;

namespace Server.Models.Dtos;

/// <summary>
/// Data transfer object for user registration.
/// </summary>
public class RegisterDto
{
    /// <summary>
    /// The email address of the user.
    /// </summary>
    public string UserEmail { get; set; } = null!;
    
    /// <summary>
    /// The password of the user.
    /// </summary>
    public string UserPassword { get; set; } = null!;

    /// <summary>
    /// The new password of the user.
    /// </summary>
    public string Confirm_UserPassword { get; set; } = null!;

    /// <summary>
    /// The list of scopes requested by the user.
    /// </summary>
    public List<string>? RequestedScopes { get; set; }
}

/// <summary>
/// Data transfer object for user login.
/// </summary>
public class LoginDto
{
    /// <summary>
    /// The email address of the user.
    /// </summary>
    public string UserEmail { get; set; } = null!;

    /// <summary>
    /// The password of the user.
    /// </summary>
    public string UserPassword { get; set; } = null!;
}

/// <summary>
/// Data transfer object for updating user information.
/// </summary>
public class UpdateUserDto
{
    /// <summary>
    /// The email address of the user.
    /// </summary>
    public string? UserEmail { get; set; }

    /// <summary>
    /// The new password of the user.
    /// </summary>
    public string? New_UserPassword { get; set; }

    /// <summary>
    /// The confirmation of the new password of the user.
    /// </summary>
    public string? Confirm_New_UserPassword { get; set; }

    /// <summary>
    /// The current password of the user.
    /// </summary>
    public string? Current_UserPassword { get; set; }

    /// <summary>
    /// The role of the user.
    /// </summary>
    public Roles? Role { get; set; }

    /// <summary>
    /// The list of scopes requested by the user.
    /// </summary>
    public List<string>? RequestedScopes { get; set; }
}