using System.Globalization;
using System.Security.Claims;

namespace Modulix.Web.Services;

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

/// <summary>
/// Represents a single user action recorded during the current login session.
/// </summary>
/// <param name="Action">The action verb or phrase (e.g., "Added", "Cancelled scan for").</param>
/// <param name="TargetName">The name of the target resource (e.g., module or endpoint name).</param>
/// <param name="Icon">The Lucide icon name (e.g., "plus", "play", "square", "trash-2").</param>
/// <param name="Tone">The visual tone of the activity entry.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the action occurred.</param>
/// <param name="TargetUrl">Optional link to the affected resource.</param>
public sealed record UserActivityItem(
    string Action,
    string TargetName,
    string Icon,
    UserActivityTone Tone,
    DateTime OccurredAtUtc,
    string? TargetUrl = null)
{
    /// <summary>
    /// Gets the CSS modifier class for the activity item.
    /// </summary>
    public string ToneClass => Tone switch
    {
        UserActivityTone.Success => "activity-success",
        UserActivityTone.Warning => "activity-warning",
        UserActivityTone.Danger => "activity-danger",
        _ => "activity-update"
    };

    /// <summary>
    /// Formats the timestamp as a human-readable relative time string.
    /// </summary>
    /// <param name="nowUtc">The current UTC time.</param>
    public string FormatRelativeTime(DateTime nowUtc)
    {
        var elapsed = nowUtc - OccurredAtUtc;
        if (elapsed.TotalSeconds < 45)
            return "Just now";
        if (elapsed.TotalMinutes < 2)
            return "1 minute ago";
        if (elapsed.TotalMinutes < 60)
            return $"{(int)elapsed.TotalMinutes} minutes ago";
        if (elapsed.TotalHours < 2)
            return "1 hour ago";
        if (elapsed.TotalHours < 24)
            return $"{(int)elapsed.TotalHours} hours ago";

        return OccurredAtUtc.ToString("MMM d, HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Tracks user-initiated actions in memory for the duration of the user's authentication session.
/// </summary>
public interface IUserActivityService
{
    /// <summary>
    /// Returns the recorded activities for the specified user's current session, ordered newest first.
    /// </summary>
    /// <param name="user">The authenticated user principal.</param>
    IReadOnlyList<UserActivityItem> GetActivities(ClaimsPrincipal user);

    /// <summary>
    /// Records a new user action for the current session.
    /// </summary>
    /// <param name="user">The authenticated user principal.</param>
    /// <param name="action">The action description (e.g., "Added", "Stopped").</param>
    /// <param name="targetName">The name of the affected module, endpoint, or resource.</param>
    /// <param name="icon">The Lucide icon name.</param>
    /// <param name="tone">The visual tone of the entry.</param>
    /// <param name="targetUrl">Optional URL to navigate to the affected item.</param>
    void Record(
        ClaimsPrincipal user,
        string action,
        string targetName,
        string icon,
        UserActivityTone tone = UserActivityTone.Update,
        string? targetUrl = null);

    /// <summary>
    /// Clears all recorded activities for the specified user's session (e.g., on sign-out).
    /// </summary>
    /// <param name="user">The authenticated user principal.</param>
    void Clear(ClaimsPrincipal user);
}
