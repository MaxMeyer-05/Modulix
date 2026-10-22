using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

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
    /// Default page size for the module endpoints list on the detail page.
    /// </summary>
    public const int DefaultEndpointsPageSize = 10;

    /// <summary>
    /// Default number of tail lines fetched for module container logs.
    /// </summary>
    public const int DefaultContainerLogsTail = 500;

    /// <summary>
    /// Allowed page size options for the module endpoints list.
    /// </summary>
    public static readonly int[] AllowedPageSizes = [5, 10, 20, 50];

    /// <summary>
    /// Allowed tail line count options for the module container logs viewer.
    /// </summary>
    public static readonly int[] AllowedLogsTailSizes = [100, 250, 500, 1000];

    /// <summary>
    /// Gets the module detail.
    /// </summary>
    public ModuleDetailDto? Module { get; private set; }

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
    /// Gets the feedback from the most recent completed module action.
    /// </summary>
    public ActionFeedback? Feedback { get; private set; }

    /// <summary>
    /// Gets the raw container log output for the module's current container.
    /// </summary>
    public string? ContainerLogs { get; private set; }

    /// <summary>
    /// Gets the error message when retrieving container logs fails.
    /// </summary>
    public string? ContainerLogsError { get; private set; }

    /// <summary>
    /// Gets the selected tail line limit for container logs.
    /// </summary>
    public int LogsTail { get; private set; } = DefaultContainerLogsTail;

    /// <summary>
    /// Gets the number of non-empty log lines currently loaded.
    /// </summary>
    public int ContainerLogLineCount { get; private set; }

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
        /// The chosen action ("activate" to keep and activate, or "remove" to delete).
        /// </summary>
        public string Action { get; set; } = "activate";
    }
}
