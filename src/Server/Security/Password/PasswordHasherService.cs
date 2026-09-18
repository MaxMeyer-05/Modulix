using Microsoft.AspNetCore.Identity;

using Server.Models.Dtos;

namespace Server.Security.Password;

/// <inheritdoc cref="IPasswordHasher"/>
public class PasswordHasherService : IPasswordHasher
{
    private readonly PasswordHasher<UserDto> _passwordHasher = new();

    /// <inheritdoc/>
    public string HashPassword(UserDto user, string password) =>
        _passwordHasher.HashPassword(user, password);

    /// <inheritdoc/>
    public bool VerifyPassword(UserDto user, string password, string hash)
    {
        var result = _passwordHasher.VerifyHashedPassword(user, hash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}