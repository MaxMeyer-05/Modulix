using Server.Models.Dtos;
using Server.Database.Entities;

namespace Server.Mappers;

/// <summary>
/// Provides mapping methods between authentication-related DTOs and entities.
/// </summary>
public static class AuthMapper
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
    /// Maps a <see cref="RegisterDto"/> to a <see cref="User"/> entity.
    /// </summary>
    /// <param name="userDto">The registration DTO.</param>
    /// <returns>A <see cref="User"/> entity representing the registration data.</returns>
    public static User ToUserEntity(this RegisterDto userDto)
    {
        return new User
        {
            UserEmail = userDto.UserEmail,
            AllowedScopes = userDto.RequestedScopes
        };
    }
}