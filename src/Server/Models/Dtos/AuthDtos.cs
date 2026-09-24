using System.ComponentModel.DataAnnotations;

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
    [Required(ErrorMessage = "User email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address.")]
    public string UserEmail { get; set; } = null!;
    
    /// <summary>
    /// The password of the user.
    /// </summary>
    [Required(ErrorMessage = "User password is required.")]
    public string UserPassword { get; set; } = null!;

    /// <summary>
    /// The new password of the user.
    /// </summary>
    [Required(ErrorMessage = "Confirm password is required.")]
    public string Confirm_UserPassword { get; set; } = null!;
}

/// <summary>
/// Data transfer object for user login.
/// </summary>
public class LoginDto
{
    /// <summary>
    /// The email address of the user.
    /// </summary>
    [Required(ErrorMessage = "User email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address.")]
    public string UserEmail { get; set; } = null!;

    /// <summary>
    /// The password of the user.
    /// </summary>
    [Required(ErrorMessage = "User password is required.")]
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
    [EmailAddress(ErrorMessage = "Invalid email address.")]
    public string? UserEmail { get; set; }
}

/// <summary>
/// Data transfer object for updating the authenticated user's password.
/// </summary>
public class UpdatePasswordDto
{
    /// <summary>
    /// The user's current password.
    /// </summary>
    [Required(ErrorMessage = "Current password is required.")]
    [MinLength(1, ErrorMessage = "Current password must be at least 1 character long.")]
    public string CurrentPassword { get; set; } = null!;

    /// <summary>
    /// The new password for the user.
    /// </summary>
    [Required(ErrorMessage = "New password is required.")]
    [MinLength(1, ErrorMessage = "New password must be at least 1 character long.")]
    public string NewPassword { get; set; } = null!;

    /// <summary>
    /// The confirmation of the new password.
    /// </summary>
    [Required(ErrorMessage = "Confirm new password is required.")]
    [MinLength(1, ErrorMessage = "Confirm new password must be at least 1 character long.")]
    public string ConfirmNewPassword { get; set; } = null!;
}

/// <summary>
/// Data transfer object for updating the user's role.
/// </summary>
public class UpdateUserRoleDto
{
    /// <summary>
    /// The new role of the user.
    /// </summary>
    [Required(ErrorMessage = "User role is required.")]
    public Roles Role { get; set; }
}

/// <summary>
/// Data transfer object for updating a user's allowed scopes.
/// </summary>
public class UpdateUserScopesDto
{
    /// <summary>
    /// The scopes assigned to the user.
    /// </summary>
    [Required(ErrorMessage = "Allowed scopes are required.")]
    public List<string> AllowedScopes { get; set; } = [];
}

/// <summary>
/// Data transfer object for deleting the authenticated user's account.
/// </summary>
public class DeleteCurrentUserDto
{
    /// <summary>
    /// The user's current password.
    /// </summary>
    [Required(ErrorMessage = "Current password is required.")]
    [MinLength(1, ErrorMessage = "Current password must be at least 1 character long.")]
    public string CurrentPassword { get; set; } = null!;
}

/// <summary>
/// Data transfer object for refreshing a token pair.
/// </summary>
public class RefreshTokenRequestDto
{
    /// <summary>
    /// The active refresh token.
    /// </summary>
    [Required(ErrorMessage = "Refresh token is required.")]
    [MinLength(1, ErrorMessage = "Refresh token must be at least 1 character long.")]
    public string RefreshToken { get; set; } = null!;
}

/// <summary>
/// Data transfer object for ending the current session.
/// </summary>
public class LogoutDto
{
    /// <summary>
    /// The refresh token for the session to revoke.
    /// </summary>
    [Required(ErrorMessage = "Refresh token is required.")]
    [MinLength(1, ErrorMessage = "Refresh token must be at least 1 character long.")]
    public string RefreshToken { get; set; } = null!;
}