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
}