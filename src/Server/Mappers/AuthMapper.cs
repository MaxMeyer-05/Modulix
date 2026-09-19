using Server.Models.Dtos;
using Server.Database.Entities;

namespace Server.Mappers;

/// <summary>
/// Provides mapping methods between authentication-related DTOs and entities.
/// </summary>
public static class AuthMapper
{
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