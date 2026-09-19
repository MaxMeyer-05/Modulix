using Server.Models.Dtos;

namespace Server.Services;

/// <summary>
/// Provides actions for authentication-related operations, 
/// including registration, login, and logout.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Registers a new user with the provided registration details.
    /// </summary>
    /// <param name="registerDto">The registration details of the new user.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when the passwords do not match.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the email is already registered.</exception>
    Task RegisterAsync(RegisterDto registerDto, CancellationToken ct);
    
    /// <summary>
    /// Logs in a user with the provided login details.
    /// </summary>
    /// <param name="loginDto">The login details of the user.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A tuple containing the user information and the token result.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the login attempt fails due to invalid credentials.</exception>
    Task<(UserDto, TokenResultDto)> LoginAsync(LoginDto loginDto, CancellationToken ct);
    
    /// <summary>
    /// Logs out the user with the specified user ID.
    /// </summary>
    /// <param name="userId">The ID of the user to log out.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when the user is not currently logged in.</exception>
    Task LogoutAsync(Guid userId, CancellationToken ct);
}