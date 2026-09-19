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
    Task<UserDto> GetUserByIdAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Retrieves all users.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A collection of user information.</returns>
    Task<IEnumerable<UserDto>> GetAllUsersAsync(CancellationToken ct);

    /// <summary>
    /// Updates the information of an existing user.
    /// </summary>
    /// <param name="userId">The ID of the user to update.</param>
    /// <param name="userDto">The updated user information.</param>
    /// <param name="ct">The cancellation token.</param>
    Task UpdateUserAsync(Guid userId, UpdateUserDto userDto, CancellationToken ct);

    /// <summary>
    /// Deletes the specified user.
    /// </summary>
    /// <param name="userId">The ID of the user to delete.</param>
    /// <param name="password">The password of the user for verification.</param>
    /// <param name="ct">The cancellation token.</param>
    Task DeleteUserAsync(Guid userId, string password, CancellationToken ct);
    
    /// <summary>
    /// Refreshes the authentication token using the provided refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The new token result.</returns>
    Task<TokenResultDto> RefreshTokenAsync(string refreshToken, CancellationToken ct);
}