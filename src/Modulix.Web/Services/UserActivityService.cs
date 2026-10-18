using System.Security.Claims;

using Microsoft.Extensions.Caching.Memory;

namespace Modulix.Web.Services;

/// <summary>
/// In-memory implementation of <see cref="IUserActivityService"/> bound to the user's OIDC/authentication session.
/// </summary>
public sealed class UserActivityService : IUserActivityService
{
    /// <summary>
    /// Claim type used to uniquely identify an authentication session in Modulix.Web.
    /// </summary>
    public const string SessionClaimType = "modulix_sid";

    private const int MaxActivitiesPerSession = 25;
    private static readonly TimeSpan IdleSessionExpiration = TimeSpan.FromHours(8);

    private readonly IMemoryCache _cache;
    private readonly Lock _sync = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="UserActivityService"/> class.
    /// </summary>
    /// <param name="cache">The in-memory cache instance.</param>
    public UserActivityService(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public IReadOnlyList<UserActivityItem> GetActivities(ClaimsPrincipal user)
    {
        var cacheKey = GetCacheKey(user);
        if (cacheKey is null)
            return [];

        lock (_sync)
        {
            if (_cache.TryGetValue<List<UserActivityItem>>(cacheKey, out var items) && items is not null)
                return items.ToList();

            return [];
        }
    }

    /// <inheritdoc />
    public void Record(
        ClaimsPrincipal user,
        string action,
        string targetName,
        string icon,
        UserActivityTone tone = UserActivityTone.Update,
        string? targetUrl = null)
    {
        var cacheKey = GetCacheKey(user);
        if (cacheKey is null || string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(targetName))
            return;

        var entry = new UserActivityItem(
            Action: action.Trim(),
            TargetName: targetName.Trim(),
            Icon: icon,
            Tone: tone,
            OccurredAtUtc: DateTime.UtcNow,
            TargetUrl: targetUrl);

        lock (_sync)
        {
            var list = _cache.GetOrCreate(cacheKey, cacheEntry =>
            {
                cacheEntry.SlidingExpiration = IdleSessionExpiration;
                return new List<UserActivityItem>();
            })!;

            list.Insert(0, entry);

            if (list.Count > MaxActivitiesPerSession)
                list.RemoveRange(MaxActivitiesPerSession, list.Count - MaxActivitiesPerSession);
        }
    }

    /// <inheritdoc />
    public void Clear(ClaimsPrincipal user)
    {
        var cacheKey = GetCacheKey(user);
        if (cacheKey is null)
            return;

        lock (_sync)
        {
            _cache.Remove(cacheKey);
        }
    }

    private static string? GetCacheKey(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return null;

        var sessionId = user.FindFirstValue(SessionClaimType)
            ?? user.FindFirstValue("sid")
            ?? user.FindFirstValue(ClaimTypes.Sid)
            ?? user.FindFirstValue("session_state")
            ?? user.FindFirstValue("sub")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.Identity.Name;

        return string.IsNullOrWhiteSpace(sessionId) ? null : $"modulix:user-activity:{sessionId}";
    }
}
