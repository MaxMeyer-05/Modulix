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
    /// <returns>The user information.</returns>
    Task<UserDto> GetUserByIdAsync(Guid userId);

    /// <summary>
    /// Retrieves all users.
    /// </summary>
    /// <returns>A collection of user information.</returns>
    Task<IEnumerable<UserDto>> GetAllUsersAsync();

    /// <summary>
    /// Updates the information of an existing user.
    /// </summary>
    /// <param name="userDto">The updated user information.</param>
    Task UpdateUserAsync(UpdateUserDto userDto);

    /// <summary>
    /// Deletes the specified user.
    /// </summary>
    /// <param name="userId">The ID of the user to delete.</param>
    /// <param name="password">The password of the user for verification.</param>
    Task DeleteUserAsync(Guid userId, string password);
    
    /// <summary>
    /// Refreshes the authentication token using the provided refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <returns>The new token result.</returns>
    Task<TokenResultDto> RefreshTokenAsync(string refreshToken);
}