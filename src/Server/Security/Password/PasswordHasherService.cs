using Microsoft.AspNetCore.Identity;

using Server.Database.Entities;

namespace Server.Security.Password;

/// <inheritdoc cref="IPasswordHasher"/>
public class PasswordHasherService : IPasswordHasher
{
    private readonly PasswordHasher<User> _passwordHasher = new();

    /// <inheritdoc/>
    public string HashPassword(User user, string password) =>
        _passwordHasher.HashPassword(user, password);

    /// <inheritdoc/>
    public bool VerifyPassword(User user, string password, string hash)
    {
        var result = _passwordHasher.VerifyHashedPassword(user, hash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}