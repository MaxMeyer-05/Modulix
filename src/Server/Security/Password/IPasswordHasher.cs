using Server.Database.Entities;

namespace Server.Security.Password;

/// <summary>
/// Provides functionality for hashing and verifying passwords.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes the specified password.
    /// </summary>
    /// <param name="user">The user for whom the password is being hashed.</param>
    /// <param name="password">The password to hash.</param>
    /// <returns>The hashed password.</returns>
    string HashPassword(User user, string password);

    /// <summary>
    /// Verifies the specified password against the given hash.
    /// </summary>
    /// <param name="user">The user whose password is to be verified.</param>
    /// <param name="password">The password to verify.</param>
    /// <param name="hash">The hash to verify against.</param>
    /// <returns><c>true</c> if the password matches the hash; otherwise, <c>false</c>.</returns>
    bool VerifyPassword(User user, string password, string hash);
}