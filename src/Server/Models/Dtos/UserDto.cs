using Server.Models.Enums;

namespace Server.Models.Dtos;

/// <summary>
/// Represents a data transfer object for a user.
/// </summary>
public class UserDto
{
    /// <summary>
    /// Defines the unique identifier for the user.
    /// </summary>
    /// <value>e.g., 3fa85f64-5717-4562-b3fc-2c963f66afa6</value>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public Guid Id { get; set; }

    /// <summary>
    /// Defines the email of the user.
    /// </summary>
    /// <value>e.g., "user@example.com"</value>
    /// <example>user@example.com</example>
    public string UserEmail { get; set; } = null!;

    /// <summary>
    /// Defines the role of the user.
    /// </summary>
    /// <value>e.g., Roles.Admin</value>
    /// <example>Roles.Admin</example>
    public Roles Role { get; set; }

    /// <summary>
    /// Defines the scopes allowed for the user.
    /// </summary>
    /// <value>e.g., ["scope1", "scope2"]</value>
    /// <example>["scope1", "scope2"]</example>
    public IEnumerable<string>? AllowedScopes { get; set; }

    /// <summary>
    /// Defines whether the user is active.
    /// </summary>
    /// <value>e.g., true, false</value>
    /// <example>true</example>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Defines the date and time when the user was created.
    /// </summary>
    /// <value>e.g., 2024-06-05T12:34:56Z</value>
    /// <example>2024-06-05T12:34:56Z</example>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}