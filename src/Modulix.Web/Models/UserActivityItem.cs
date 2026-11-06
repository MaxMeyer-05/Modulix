using System.Globalization;

namespace Modulix.Web.Models;

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
