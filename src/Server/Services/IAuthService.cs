using Server.Models.Dtos;

namespace Server.Services;

/// <summary>
/// Provides an interface for authentication-related operations, 
/// including registration, login, and logout.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Registers a new user with the provided registration details.
    /// </summary>
    /// <param name="registerDto">The registration details of the new user.</param>
    /// <exception cref="ArgumentException">Thrown when the passwords do not match.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the email is already registered.</exception>
    Task RegisterAsync(RegisterDto registerDto);
    
    /// <summary>
    /// Logs in a user with the provided login details.
    /// </summary>
    /// <param name="loginDto">The login details of the user.</param>
    /// <returns>A tuple containing the user information and the token result.</returns>
    Task<(UserDto, TokenResultDto)> LoginAsync(LoginDto loginDto);
    
    /// <summary>
    /// Logs out the user with the specified user ID.
    /// </summary>
    /// <param name="userId">The ID of the user to log out.</param>
    Task LogoutAsync(Guid userId);
}