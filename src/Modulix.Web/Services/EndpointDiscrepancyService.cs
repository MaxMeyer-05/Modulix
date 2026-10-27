using Microsoft.Extensions.Caching.Memory;

using Modulix.Client.Models.Dtos;

namespace Modulix.Web.Services;

/// <summary>
/// In-memory implementation of <see cref="IEndpointDiscrepancyService"/> backed by <see cref="IMemoryCache"/>.
/// </summary>
public sealed class EndpointDiscrepancyService : IEndpointDiscrepancyService
{
    private static readonly TimeSpan ReportExpiration = TimeSpan.FromHours(24);

    private readonly IMemoryCache _cache;
    private readonly Lock _sync = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="EndpointDiscrepancyService"/> class.
    /// </summary>
    /// <param name="cache">The in-memory cache instance.</param>
    public EndpointDiscrepancyService(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public SubEndpointDiscrepancyReportDto? GetReport(Guid moduleId)
    {
        if (moduleId == Guid.Empty)
            return null;

        lock (_sync)
        {
            if (_cache.TryGetValue<SubEndpointDiscrepancyReportDto>(GetCacheKey(moduleId), out var report) && report is not null)
                return CloneReport(report);

            return null;
        }
    }

    /// <inheritdoc />
    public void SaveReport(Guid moduleId, SubEndpointDiscrepancyReportDto? report)
    {
        if (moduleId == Guid.Empty)
            return;

        lock (_sync)
        {
            var cacheKey = GetCacheKey(moduleId);
            if (report is null || !report.HasDiscrepancy)
            {
                _cache.Remove(cacheKey);
                return;
            }

            _cache.Set(cacheKey, CloneReport(report), new MemoryCacheEntryOptions
            {
                SlidingExpiration = ReportExpiration
            });
        }
    }

    /// <inheritdoc />
    public void RemoveEndpoints(Guid moduleId, IEnumerable<DiscoveredSubEndpointDto> resolvedEndpoints)
    {
        if (moduleId == Guid.Empty || resolvedEndpoints is null)
            return;

        var resolvedList = resolvedEndpoints.ToList();
        if (resolvedList.Count == 0)
            return;

        lock (_sync)
        {
            var cacheKey = GetCacheKey(moduleId);
            if (!_cache.TryGetValue<SubEndpointDiscrepancyReportDto>(cacheKey, out var report) || report is null)
                return;

            var remainingExtra = report.ExtraEndpoints?
                .Where(ep => !resolvedList.Any(r => EndpointsMatch(ep, r)))
                .ToList();

            var remainingMissing = report.MissingEndpoints?
                .Where(ep => !resolvedList.Any(r => EndpointsMatch(ep, r)))
                .ToList();

            var hasRemainingDiscrepancy = (remainingExtra?.Count ?? 0) > 0 || (remainingMissing?.Count ?? 0) > 0;
            if (!hasRemainingDiscrepancy)
            {
                _cache.Remove(cacheKey);
                return;
            }

            report.ExtraEndpoints = remainingExtra;
            report.MissingEndpoints = remainingMissing;
            report.HasDiscrepancy = true;
        }
    }

    /// <inheritdoc />
    public void ClearReport(Guid moduleId)
    {
        if (moduleId == Guid.Empty)
            return;

        lock (_sync)
        {
            _cache.Remove(GetCacheKey(moduleId));
        }
    }

    /// <summary>
    /// Determines whether two <see cref="DiscoveredSubEndpointDto"/> instances refer to the same HTTP method and route path.
    /// </summary>
    public static bool EndpointsMatch(DiscoveredSubEndpointDto left, DiscoveredSubEndpointDto right) =>
        EndpointsMatch(left.HttpMethod?.Method, left.EndpointPath, right.HttpMethod?.Method, right.EndpointPath);

    /// <summary>
    /// Determines whether two HTTP method and route path pairs refer to the same endpoint.
    /// </summary>
    public static bool EndpointsMatch(string? leftMethod, string? leftPath, string? rightMethod, string? rightPath)
    {
        if (string.IsNullOrWhiteSpace(leftMethod) || string.IsNullOrWhiteSpace(rightMethod) ||
            string.IsNullOrWhiteSpace(leftPath) || string.IsNullOrWhiteSpace(rightPath))
        {
            return false;
        }

        var normalizedLeftPath = "/" + leftPath.Trim().TrimStart('/');
        var normalizedRightPath = "/" + rightPath.Trim().TrimStart('/');

        return string.Equals(leftMethod.Trim(), rightMethod.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(normalizedLeftPath, normalizedRightPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCacheKey(Guid moduleId) => $"modulix:discrepancy-report:{moduleId:D}";

    private static SubEndpointDiscrepancyReportDto CloneReport(SubEndpointDiscrepancyReportDto source) => new()
    {
        ModuleId = source.ModuleId,
        HasDiscrepancy = source.HasDiscrepancy,
        MatchedEndpoints = source.MatchedEndpoints?.Select(CloneEndpoint).ToList(),
        ExtraEndpoints = source.ExtraEndpoints?.Select(CloneEndpoint).ToList(),
        MissingEndpoints = source.MissingEndpoints?.Select(CloneEndpoint).ToList()
    };

    private static DiscoveredSubEndpointDto CloneEndpoint(DiscoveredSubEndpointDto endpoint) => new()
    {
        HttpMethod = new HttpMethod(endpoint.HttpMethod.Method),
        EndpointPath = endpoint.EndpointPath
    };
}

