using System.Security.Claims;

using Modulix.Web.Models;

namespace Modulix.Web.Services.Interfaces;

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
