using Server.Models.Dtos;

namespace Server.Services;

/// <summary>
/// Provides actions for user-related operations, 
/// including retrieving, updating, and deleting user information, 
/// as well as refreshing authentication tokens.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Retrieves the user information for the specified user ID.
    /// </summary>
    /// <param name="userId">The ID of the user to retrieve.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The user information.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the user with the specified ID does not exist.</exception>
    Task<UserDto> GetUserByIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all users.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A collection of user information.</returns>
    Task<IEnumerable<UserDto>> GetAllUsersAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates the information of an existing user.
    /// </summary>
    /// <param name="userId">The ID of the user to update.</param>
    /// <param name="userDto">The updated user information.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The new token result when an update invalidates existing tokens; otherwise, <see langword="null"/>.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the user with the specified ID does not exist.</exception>
    Task<TokenResultDto?> UpdateUserAsync(Guid userId, UpdateUserDto userDto, CancellationToken ct = default);

    /// <summary>
    /// Updates a user's password.
    /// </summary>
    /// <param name="userId">The ID of the user to update.</param>
    /// <param name="passwordDto">The current password and new password details.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A new token result for the authenticated user.</returns>
    /// <exception cref="ArgumentException">Thrown if the new password confirmation does not match.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if the user with the specified ID does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if the current password is invalid.</exception>
    Task<TokenResultDto> UpdatePasswordAsync(Guid userId, UpdatePasswordDto passwordDto, CancellationToken ct = default);

    /// <summary>
    /// Updates the role of an existing user.
    /// </summary>
    /// <param name="userId">The ID of the user to update.</param>
    /// <param name="userRoleDto">The updated user role information.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Thrown if the user with the specified ID does not exist.</exception>
    Task UpdateUserRoleAsync(Guid userId, UpdateUserRoleDto userRoleDto, CancellationToken ct = default);

    /// <summary>
    /// Updates the allowed scopes of an existing user.
    /// </summary>
    /// <param name="userId">The ID of the user to update.</param>
    /// <param name="userScopesDto">The updated user scopes.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Thrown if the user with the specified ID does not exist.</exception>
    Task UpdateUserScopesAsync(Guid userId, UpdateUserScopesDto userScopesDto, CancellationToken ct = default);

    /// <summary>
    /// Deletes the specified user.
    /// </summary>
    /// <param name="userId">The ID of the user to delete.</param>
    /// <param name="password">The password of the user for verification.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Thrown if the user with the specified ID does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if the provided password is invalid.</exception>
    Task DeleteUserAsync(Guid userId, string? password, CancellationToken ct = default);
    
    /// <summary>
    /// Refreshes the authentication token using the provided refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The new token result.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the provided refresh token is invalid.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if the user associated with the refresh token does not exist.</exception>
    Task<TokenResultDto> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);
}