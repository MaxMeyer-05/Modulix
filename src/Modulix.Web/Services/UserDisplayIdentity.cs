using System.Security.Claims;

namespace Modulix.Web.Services;

/// <summary>
/// Display information for the signed-in user, resolved from the identity provider claims.
/// </summary>
/// <param name="GivenName">The first name, if available.</param>
/// <param name="FamilyName">The last name, if available.</param>
/// <param name="FullName">The first and last name, or a fallback name such as the username.</param>
/// <param name="UserName">The login name or email address, if it differs from <paramref name="FullName"/>.</param>
/// <param name="Initials">One or two initials for an avatar.</param>
public sealed record UserDisplayIdentity(
    string? GivenName,
    string? FamilyName,
    string? FullName,
    string? UserName,
    string Initials)
{
    /// <summary>
    /// Resolves the display information from the claims of <paramref name="user"/>.
    /// </summary>
    public static UserDisplayIdentity FromPrincipal(ClaimsPrincipal user)
    {
        var givenName = Clean(user.FindFirstValue(ClaimTypes.GivenName) ?? user.FindFirstValue("given_name"));
        var familyName = Clean(user.FindFirstValue(ClaimTypes.Surname) ?? user.FindFirstValue("family_name"));
        var userName = Clean(user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue("email"));

        var combinedName = string.Join(" ", new[] { givenName, familyName }.Where(part => part is not null));
        var fullName = !string.IsNullOrEmpty(combinedName)
            ? combinedName
            : Clean(user.FindFirstValue("name")
                ?? user.FindFirstValue(ClaimTypes.Name)
                ?? userName
                ?? user.Identity?.Name);

        string initials;
        if (givenName is not null && familyName is not null)
        {
            initials = $"{char.ToUpperInvariant(givenName[0])}{char.ToUpperInvariant(familyName[0])}";
        }
        else if (fullName is not null)
        {
            var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            initials = parts.Length >= 2
                ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}"
                : char.ToUpperInvariant(parts[0][0]).ToString();
        }
        else
        {
            initials = "U";
        }

        if (string.Equals(userName, fullName, StringComparison.OrdinalIgnoreCase))
            userName = null;

        return new UserDisplayIdentity(givenName, familyName, fullName, userName, initials);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
