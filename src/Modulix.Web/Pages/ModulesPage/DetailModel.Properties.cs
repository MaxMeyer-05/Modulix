using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;
using Modulix.Client.Models.Enums;
using Modulix.Web.Services;

namespace Modulix.Web.Pages.ModulesPage;

/// <summary>
/// Properties, computed view flags, and data types for the module detail page.
/// </summary>
public partial class DetailModel
{
    /// <summary>
    /// TempData key used for module action feedback across redirects.
    /// </summary>
    public const string FeedbackKey = "ModuleActionFeedback";

    /// <summary>
    /// TempData key used for preserving the endpoint discrepancy report across redirects.
    /// </summary>
    public const string DiscrepancyReportKey = "ModuleDiscrepancyReport";

    /// <summary>
    /// Default page size for the module endpoints list on the detail page.
    /// </summary>
    public const int DefaultEndpointsPageSize = 10;

    /// <summary>
    /// Allowed page size options for the module endpoints list.
    /// </summary>
    public static readonly int[] AllowedPageSizes = [5, 10, 20, 50];

    /// <summary>
    /// Gets the module detail.
    /// </summary>
    public ModuleDetailDto? Module { get; private set; }

    /// <summary>
    /// Gets the live container resource metrics and execution state for the module, if available.
    /// </summary>
    public ModuleStatsDto? ModuleStats { get; private set; }

    /// <summary>
    /// Gets the endpoint discrepancy report associated with the module's pending confirmation state, if available.
    /// </summary>
    public SubEndpointDiscrepancyReportDto? DiscrepancyReport { get; private set; }

    /// <summary>
    /// Gets the paged sub-endpoints for the current page of the module detail view.
    /// </summary>
    public IReadOnlyList<ModuleSubEndpointDto> Endpoints { get; private set; } = [];

    /// <summary>
    /// Gets the total number of sub-endpoints registered for the module.
    /// </summary>
    public int TotalEndpoints { get; private set; }

    /// <summary>
    /// Gets the current 1-based page number for the module endpoints list.
    /// </summary>
    public int CurrentPage { get; private set; } = PaginationQueryDto.DefaultPage;

    /// <summary>
    /// Gets the number of endpoints displayed per page.
    /// </summary>
    public int PageSize { get; private set; } = DefaultEndpointsPageSize;

    /// <summary>
    /// Gets the total number of endpoint pages available.
    /// </summary>
    public int TotalPages { get; private set; } = 1;

    /// <summary>
    /// Indicates whether there is a previous page of endpoints.
    /// </summary>
    public bool HasPreviousPage { get; private set; }

    /// <summary>
    /// Indicates whether there is a next page of endpoints.
    /// </summary>
    public bool HasNextPage { get; private set; }

    /// <summary>
    /// Gets the submitted edit form values when an update failed.
    /// </summary>
    public UpdateModuleDto? EditInput { get; private set; }

    /// <summary>
    /// Gets the error from a failed metadata update attempt.
    /// </summary>
    public ActionError? EditError { get; private set; }

    /// <summary>
    /// Gets the submitted version update form values when a file update failed.
    /// </summary>
    public ModuleVersionCreateDto? UpdateFilesInput { get; private set; }

    /// <summary>
    /// Gets the error from a failed module file update attempt.
    /// </summary>
    public ActionError? UpdateFilesError { get; private set; }

    /// <summary>
    /// Gets the selected target version when a rollback attempt failed.
    /// </summary>
    public string? RollbackVersion { get; private set; }

    /// <summary>
    /// Gets the error from a failed module rollback attempt.
    /// </summary>
    public ActionError? RollbackError { get; private set; }

    /// <summary>
    /// Gets the error from a failed endpoint confirmation attempt.
    /// </summary>
    public ActionError? ConfirmEndpointsError { get; private set; }

    /// <summary>
    /// Gets the error from a failed module deletion attempt.
    /// </summary>
    public ActionError? DeleteError { get; private set; }

    /// <summary>
    /// Gets the original version string targeted by a failed version edit attempt.
    /// </summary>
    public string? EditVersionTarget { get; private set; }

    /// <summary>
    /// Gets the submitted version metadata when a version edit attempt failed.
    /// </summary>
    public ModuleVersionUpdateDto? EditVersionInput { get; private set; }

    /// <summary>
    /// Gets the error from a failed version metadata update attempt.
    /// </summary>
    public ActionError? EditVersionError { get; private set; }

    /// <summary>
    /// Gets the target version string when a version deletion attempt failed.
    /// </summary>
    public string? DeleteVersionTarget { get; private set; }

    /// <summary>
    /// Gets the error from a failed version deletion attempt.
    /// </summary>
    public ActionError? DeleteVersionError { get; private set; }

    /// <summary>
    /// Gets the feedback from the most recent completed module action.
    /// </summary>
    public ActionFeedback? Feedback { get; private set; }

    /// <summary>
    /// Indicates whether the edit dialog should be reopened due to an error.
    /// </summary>
    public bool ReopenEdit => EditError is not null;

    /// <summary>
    /// Indicates whether the update files dialog should be reopened due to an error.
    /// </summary>
    public bool ReopenUpdateFiles => UpdateFilesError is not null;

    /// <summary>
    /// Indicates whether the rollback dialog should be reopened due to an error.
    /// </summary>
    public bool ReopenRollback => RollbackError is not null;

    /// <summary>
    /// Indicates whether the confirm endpoints dialog should be reopened due to an error.
    /// </summary>
    public bool ReopenConfirmEndpoints => ConfirmEndpointsError is not null;

    /// <summary>
    /// Indicates whether the delete dialog should be reopened due to an error.
    /// </summary>
    public bool ReopenDelete => DeleteError is not null;

    /// <summary>
    /// Indicates whether the edit version dialog should be reopened due to an error.
    /// </summary>
    public bool ReopenEditVersion => EditVersionError is not null;

    /// <summary>
    /// Indicates whether the delete version dialog should be reopened due to an error.
    /// </summary>
    public bool ReopenDeleteVersion => DeleteVersionError is not null;

    /// <summary>
    /// Indicates whether the module can currently be started.
    /// </summary>
    public bool CanStart => Module?.Status is ModuleStatus.Stopped
        or ModuleStatus.Created
        or ModuleStatus.Failed;

    /// <summary>
    /// Indicates whether the module can currently be stopped.
    /// </summary>
    public bool CanStop => Module?.Status is ModuleStatus.Running
        or ModuleStatus.Starting;

    /// <summary>
    /// Indicates whether live container resource statistics should be displayed.
    /// </summary>
    public bool ShowContainerStats => Module?.Status == ModuleStatus.Running || ModuleStats?.IsRunning == true;

    /// <summary>
    /// Gets the formatted CPU usage percentage for the running container.
    /// </summary>
    public string FormattedCpuUsage => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{(ModuleStats?.CpuUsagePercent ?? 0):0.00}%");

    /// <summary>
    /// Gets the formatted memory usage and limit for the running container.
    /// </summary>
    public string FormattedMemoryUsage => ModuleStats is { MemoryLimitMiB: > 0 } stats
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{stats.MemoryUsageMiB:0.00} MiB / {stats.MemoryLimitMiB:0.00} MiB")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(ModuleStats?.MemoryUsageMiB ?? 0):0.00} MiB");

    /// <summary>
    /// Gets the formatted container uptime for the running container.
    /// </summary>
    public string FormattedUptime
    {
        get
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, ModuleStats?.UptimeSeconds ?? 0));
            if (span.TotalDays >= 1)
                return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m {span.Seconds}s");
            if (span.TotalHours >= 1)
                return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s");
            if (span.TotalMinutes >= 1)
                return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}m {span.Seconds}s");

            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)Math.Round(span.TotalSeconds)}s");
        }
    }

    /// <summary>
    /// Gets the module versions ordered from newest to oldest.
    /// </summary>
    public IReadOnlyList<ModuleVersionDto> OrderedVersions => Module?.Versions
        .OrderByDescending(v => v.ReleasedAtUtc)
        .ThenByDescending(v => v.Id)
        .ToList() ?? [];

    /// <summary>
    /// Gets the active version metadata for the module.
    /// </summary>
    public ModuleVersionDto? CurrentVersion => OrderedVersions.FirstOrDefault(v =>
        string.Equals(v.Version, Module?.CurrentVersion, StringComparison.Ordinal));

    /// <summary>
    /// Gets the previous versions available as rollback targets.
    /// </summary>
    public IReadOnlyList<ModuleVersionDto> RollbackVersions => OrderedVersions
        .Where(v => !string.Equals(v.Version, Module?.CurrentVersion, StringComparison.Ordinal))
        .ToList();

    /// <summary>
    /// Gets the endpoints that currently require confirmation.
    /// </summary>
    public IReadOnlyList<ModuleSubEndpointDto> PendingEndpoints => Module?.Endpoints
        .Where(e => e.Status == ModuleEndpointsStatus.PendingConfirmation)
        .ToList() ?? [];

    /// <summary>
    /// Indicates whether the module has any unconfirmed endpoints.
    /// </summary>
    public bool HasPendingEndpoints => Module?.Status == ModuleStatus.PendingConfirmation
        || PendingEndpoints.Count > 0;

    /// <summary>
    /// Indicates whether pending endpoints can currently be confirmed.
    /// </summary>
    public bool CanConfirmEndpoints => Module?.Status == ModuleStatus.PendingConfirmation
        && PendingEndpoints.Count > 0;

    /// <summary>
    /// Determines whether the specified inactive sub-endpoint can currently be activated.
    /// </summary>
    /// <param name="endpoint">The sub-endpoint to evaluate.</param>
    public bool CanActivateEndpoint(ModuleSubEndpointDto endpoint)
    {
        if (Module?.Status != ModuleStatus.Running || endpoint.Status != ModuleEndpointsStatus.Inactive)
            return false;

        if (CurrentVersion is null || string.IsNullOrWhiteSpace(endpoint.DiscoveredInVersion))
            return true;

        if (string.Equals(endpoint.DiscoveredInVersion, CurrentVersion.Version, StringComparison.Ordinal))
            return true;

        var endpointVersion = OrderedVersions.FirstOrDefault(v =>
            string.Equals(v.Version, endpoint.DiscoveredInVersion, StringComparison.Ordinal));

        return endpointVersion is null ||
            endpointVersion.ReleasedAtUtc < CurrentVersion.ReleasedAtUtc ||
            (endpointVersion.ReleasedAtUtc == CurrentVersion.ReleasedAtUtc && endpointVersion.Id <= CurrentVersion.Id);
    }

    /// <summary>
    /// Indicates whether a discrepancy report with active discrepancies is available for this module.
    /// </summary>
    public bool HasDiscrepancyReport => DiscrepancyReport?.HasDiscrepancy == true
        && (ExtraDiscrepancyEndpoints.Count > 0 || MissingDiscrepancyEndpoints.Count > 0);

    /// <summary>
    /// Gets the endpoints from <see cref="DiscrepancyReport"/> that matched the expected configuration.
    /// </summary>
    public IReadOnlyList<DiscoveredSubEndpointDto> MatchedDiscrepancyEndpoints =>
        DiscrepancyReport?.MatchedEndpoints ?? [];

    /// <summary>
    /// Gets the extra endpoints from <see cref="DiscrepancyReport"/> that were discovered by the scanner but not specified by the user.
    /// </summary>
    public IReadOnlyList<DiscoveredSubEndpointDto> ExtraDiscrepancyEndpoints =>
        FilterPendingDiscrepancyEndpoints(DiscrepancyReport?.ExtraEndpoints);

    /// <summary>
    /// Gets the missing endpoints from <see cref="DiscrepancyReport"/> that were specified by the user but not found by the scanner.
    /// </summary>
    public IReadOnlyList<DiscoveredSubEndpointDto> MissingDiscrepancyEndpoints =>
        FilterPendingDiscrepancyEndpoints(DiscrepancyReport?.MissingEndpoints);

    /// <summary>
    /// Determines the discrepancy category (<see cref="EndpointDiscrepancyKind"/>) for a sub-endpoint.
    /// </summary>
    /// <param name="httpMethod">The HTTP method of the endpoint.</param>
    /// <param name="endpointPath">The relative route path of the endpoint.</param>
    /// <param name="discoveredInVersion">Optional version string in which the endpoint was registered or discovered.</param>
    public EndpointDiscrepancyKind GetEndpointDiscrepancyKind(
        string httpMethod,
        string endpointPath,
        string? discoveredInVersion = null)
    {
        var method = httpMethod;
        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(endpointPath))
            return EndpointDiscrepancyKind.None;

        if (DiscrepancyReport is not null)
        {
            if (DiscrepancyReport.ExtraEndpoints?.Any(ep =>
                EndpointDiscrepancyService.EndpointsMatch(ep.HttpMethod, ep.EndpointPath, method, endpointPath)) == true)
            {
                return EndpointDiscrepancyKind.Extra;
            }

            if (DiscrepancyReport.MissingEndpoints?.Any(ep =>
                EndpointDiscrepancyService.EndpointsMatch(ep.HttpMethod, ep.EndpointPath, method, endpointPath)) == true)
            {
                return EndpointDiscrepancyKind.Missing;
            }

            if (DiscrepancyReport.MatchedEndpoints?.Any(ep =>
                EndpointDiscrepancyService.EndpointsMatch(ep.HttpMethod, ep.EndpointPath, method, endpointPath)) == true)
            {
                return EndpointDiscrepancyKind.Matched;
            }
        }

        if (Module is not null &&
            !string.IsNullOrWhiteSpace(discoveredInVersion) &&
            !string.IsNullOrWhiteSpace(Module.CurrentVersion))
        {
            if (!string.Equals(discoveredInVersion, Module.CurrentVersion, StringComparison.OrdinalIgnoreCase))
                return EndpointDiscrepancyKind.Missing;

            if (OrderedVersions.Count > 1 &&
                string.Equals(discoveredInVersion, Module.CurrentVersion, StringComparison.OrdinalIgnoreCase))
            {
                return EndpointDiscrepancyKind.Extra;
            }
        }

        return EndpointDiscrepancyKind.None;
    }

    private IReadOnlyList<DiscoveredSubEndpointDto> FilterPendingDiscrepancyEndpoints(
        IReadOnlyList<DiscoveredSubEndpointDto>? candidates)
    {
        if (candidates is null || candidates.Count == 0)
            return [];

        var pending = PendingEndpoints;
        if (pending.Count == 0)
            return candidates;

        return candidates
            .Where(c => pending.Any(p =>
                EndpointDiscrepancyService.EndpointsMatch(c.HttpMethod, c.EndpointPath, p.HttpMethod, p.EndpointPath)))
            .ToList();
    }

    /// <summary>
    /// Indicates whether the module can currently be rolled back to an earlier version.
    /// </summary>
    public bool CanRollback => Module is not null
        && RollbackVersions.Count > 0
        && Module.Status != ModuleStatus.QueuedForScan
        && !HasPendingEndpoints;

    /// <summary>
    /// Gets the tooltip description for the rollback action button.
    /// </summary>
    public string RollbackTitle => Module is null
        ? "Module not available"
        : Module.Status == ModuleStatus.QueuedForScan
            ? "Cannot roll back module while queued for scanning"
            : HasPendingEndpoints
                ? "Confirm pending endpoints before rolling back"
                : RollbackVersions.Count == 0
                    ? "No previous versions available to roll back to"
                    : "Roll back this module to an earlier version";

    /// <summary>
    /// Represents a failed module action error shown inside a dialog.
    /// </summary>
    public sealed record ActionError(int StatusCode, string Title, string Description);

    /// <summary>
    /// Represents feedback from a completed module action.
    /// </summary>
    public sealed record ActionFeedback(int StatusCode, string Title, string Description, bool IsSuccess);

    /// <summary>
    /// Represents a user decision for confirming or removing a pending module endpoint.
    /// </summary>
    public sealed class EndpointConfirmationDecision
    {
        /// <summary>
        /// The HTTP method of the pending endpoint.
        /// </summary>
        public string HttpMethod { get; set; } = string.Empty;

        /// <summary>
        /// The relative path of the pending endpoint.
        /// </summary>
        public string EndpointPath { get; set; } = string.Empty;

        /// <summary>
        /// The chosen action ("remove" to delete, or "activate" to keep and activate).
        /// Defaults to "remove" so discrepancy endpoints are not activated unintentionally.
        /// </summary>
        public string Action { get; set; } = "remove";
    }
}
