namespace Modulix.Web.Models;

/// <summary>
/// Represents the visual tone of a user activity entry.
/// </summary>
public enum UserActivityTone
{
    /// <summary>
    /// Positive or completed action (e.g., module created, started, endpoints confirmed).
    /// </summary>
    Success,

    /// <summary>
    /// Informational or update action (e.g., module queued for scan, files updated).
    /// </summary>
    Update,

    /// <summary>
    /// Cautionary or destructive action (e.g., module stopped, scan cancelled, log deleted).
    /// </summary>
    Warning,

    /// <summary>
    /// Critical or deletion action (e.g., module or endpoint deleted).
    /// </summary>
    Danger
}
