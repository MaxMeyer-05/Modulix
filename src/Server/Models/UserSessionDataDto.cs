using Server.Security;

namespace Server.Models;

/// <summary>
/// Represents the session data for a user.
/// </summary>
public class UserSessionDataDto
{
    /// <summary>
    /// Defines the unique identifier of the user.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Defines the role of the user.
    /// </summary>
    public Roles Role { get; set; }

    /// <summary>
    /// Defines the scopes assigned to the user.
    /// </summary>
    public IReadOnlyList<string>? Scopes { get; set; }

    /// <summary>
    /// Defines the claims associated with the user.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Claims { get; set; }
}