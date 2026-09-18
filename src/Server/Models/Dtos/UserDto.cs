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
    public Guid Id { get; set; }

    /// <summary>
    /// Defines the email of the user.
    /// </summary>
    public string UserEmail { get; set; } = null!;

    /// <summary>
    /// Defines the hashed password of the user.
    /// </summary>
    public string PasswordHash { get; set; } = null!;

    /// <summary>
    /// Defines the role of the user.
    /// </summary>
    public Roles Role { get; set; }

    /// <summary>
    /// Defines the scopes allowed for the user.
    /// </summary>
    public IEnumerable<string> AllowedScopes { get; set; } = [];

    /// <summary>
    /// Defines whether the user is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Defines the date and time when the user was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}