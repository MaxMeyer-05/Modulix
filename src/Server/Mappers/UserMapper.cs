using Server.Models.Dtos;
using Server.Database.Entities;

namespace Server.Mappers;

/// <summary>
/// Provides mapping functions for converting between user-related data models and DTOs.
/// </summary>
public static class UserMapper
{
    /// <summary>
    /// Maps a <see cref="User"/> entity to a <see cref="UserDto"/>.
    /// </summary>
    /// <param name="user">The user entity.</param>
    /// <returns>A <see cref="UserDto"/> representing the user.</returns>
    public static UserDto ToUserDto(this User user)
    {
        return new UserDto
        {
            Id = user.Id,
            UserEmail = user.UserEmail,
            Role = user.Role,
            AllowedScopes = user.AllowedScopes,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt
        };
    }

    /// <summary>
    /// Updates a <see cref="User"/> entity with the values from an <see cref="UpdateUserDto"/>.
    /// </summary>
    /// <param name="user">The user entity to update.</param>
    /// <param name="userDto">The DTO containing the updated user information.</param>
    public static void UpdateUserFromDto(this User user, UpdateUserDto userDto)
    {
        user.UserEmail = userDto.UserEmail ?? user.UserEmail;
        user.Role = userDto.Role ?? user.Role;
        user.AllowedScopes = userDto.RequestedScopes ?? user.AllowedScopes;
    }
}