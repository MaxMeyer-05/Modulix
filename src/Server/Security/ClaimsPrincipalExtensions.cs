using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

using Server.Models;

namespace Server.Security;

/// <summary>
/// Populates the given UserSessionDataDto with information from the ClaimsPrincipal.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Populates the UserSessionDataDto with the user's ID, role, scopes, and claims from the ClaimsPrincipal.
    /// </summary>
    /// <param name="principal">The ClaimsPrincipal containing the user's claims.</param>
    /// <param name="session">The UserSessionDataDto to populate with the user's session data.</param>
    public static void PopulateSessionData(this ClaimsPrincipal principal, UserSessionDataDto session)
    {
        var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value 
                  ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (Guid.TryParse(sub, out var parsedGuid))
        {
            session.UserId = parsedGuid;
        }

        var roleClaim = principal.FindFirst(ClaimTypes.Role)?.Value 
                        ?? principal.FindFirst("role")?.Value;

        if (Enum.TryParse<Roles>(roleClaim, ignoreCase: true, out var parsedRole)
            && Enum.IsDefined(parsedRole))
        {
            session.Role = parsedRole;
        }

        session.Scopes = principal.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct()
            .ToList();

        session.Claims = principal.Claims
            .GroupBy(c => c.Type)
            .ToDictionary(
                g => g.Key, 
                g => string.Join(", ", g.Select(c => c.Value))
            );
    }

    /// <summary>
    /// Determines whether the principal contains the required JWT session claims.
    /// </summary>
    /// <param name="principal">The principal to validate.</param>
    /// <returns><see langword="true"/> when the subject and role are valid.</returns>
    public static bool HasValidSessionClaims(this ClaimsPrincipal principal)
    {
        var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(subject, out _))
        {
            return false;
        }

        var roleClaim = principal.FindFirst(ClaimTypes.Role)?.Value
            ?? principal.FindFirst("role")?.Value;

        return Enum.TryParse<Roles>(roleClaim, ignoreCase: true, out var role)
            && Enum.IsDefined(role);
    }
}