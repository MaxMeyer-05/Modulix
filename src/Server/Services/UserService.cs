using Server.Models.Dtos;

namespace Server.Services;

/// <inheritdoc cref="IUserService"/>
public class UserService : IUserService
{
    /// <inheritdoc/>
    public async Task DeleteUserAsync(Guid userId, string password)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<UserDto>> GetAllUsersAsync()
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public async Task<UserDto> GetUserByIdAsync(Guid userId)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public async Task<TokenResultDto> RefreshTokenAsync(string refreshToken)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public async Task UpdateUserAsync(UpdateUserDto userDto)
    {
        throw new NotImplementedException();
    }
}