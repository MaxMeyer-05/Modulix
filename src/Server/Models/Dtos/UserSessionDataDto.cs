using Server.Models.Enums;

namespace Server.Models.Dtos;

/// <summary>
/// Represents the session data for a user.
/// </summary>
public class UserSessionDataDto
{
    /// <summary>
    /// Defines the unique identifier of the user.
    /// </summary>
    /// <value>e.g., 3fa85f64-5717-4562-b3fc-2c963f66afa6</value>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public Guid UserId { get; set; }

    /// <summary>
    /// Defines the role of the user.
    /// </summary>
    /// <value>e.g., Roles.Admin</value>
    /// <example>Roles.Admin</example>
    public Roles Role { get; set; }

    /// <summary>
    /// Defines the scopes assigned to the user.
    /// </summary>
    /// <value>e.g., ["scope1", "scope2"]</value>
    /// <example>["scope1", "scope2"]</example>
    public IReadOnlyList<string>? Scopes { get; set; }

    /// <summary>
    /// Defines the claims associated with the user.
    /// </summary>
    /// <value>e.g., {"claim1": "value1", "claim2": "value2"}</value>
    /// <example>{"claim1": "value1", "claim2": "value2"}</example>
    public IReadOnlyDictionary<string, string>? Claims { get; set; }
}