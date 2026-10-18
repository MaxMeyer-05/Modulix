using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

using Modulix.Client;
using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;
using Modulix.Web.Services;

namespace Modulix.Web.Pages.ScanQueuePage;

/// <summary>
/// Handles the display and management of the module scan queue page.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PageModel
{
    private const string FeedbackKey = "ScanQueueFeedback";

    /// <summary>
    /// The Modulix API client.
    /// </summary>
    private readonly IModulixApi _modulixApiClient;

    /// <summary>
    /// The user session activity service.
    /// </summary>
    private readonly IUserActivityService _userActivityService;

    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<IndexModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexModel"/> class.
    /// </summary>
    /// <param name="modulixApiClient">The Modulix API client.</param>
    /// <param name="userActivityService">The user session activity service.</param>
    /// <param name="logger">The logger instance.</param>
    public IndexModel(
        IModulixApi modulixApiClient,
        IUserActivityService userActivityService,
        ILogger<IndexModel> logger)
    {
        _modulixApiClient = modulixApiClient;
        _userActivityService = userActivityService;
        _logger = logger;
    }

    /// <summary>
    /// The filtered list of scan queue entries with their 1-based queue position.
    /// </summary>
    public List<QueueEntryView> QueueEntries { get; private set; } = [];

    /// <summary>
    /// Total number of items in the scan queue before filtering.
    /// </summary>
    public int TotalCount { get; private set; }

    /// <summary>
    /// Number of currently running scans.
    /// </summary>
    public int RunningCount { get; private set; }

    /// <summary>
    /// Number of scans waiting for an available scanner slot.
    /// </summary>
    public int WaitingCount { get; private set; }

    /// <summary>
    /// Number of high-priority (Patch) scans in the queue.
    /// </summary>
    public int PatchPriorityCount { get; private set; }

    /// <summary>
    /// The current search query.
    /// </summary>
    public string Query { get; private set; } = "";

    /// <summary>
    /// The current priority filter.
    /// </summary>
    public string PriorityFilter { get; private set; } = "";

    /// <summary>
    /// The current execution state filter.
    /// </summary>
    public string StateFilter { get; private set; } = "";

    /// <summary>
    /// Feedback from the last scan cancellation action.
    /// </summary>
    public ActionFeedback? Feedback { get; private set; }

    /// <summary>
    /// Handles the GET request for the scan queue page.
    /// </summary>
    /// <param name="q">The search query.</param>
    /// <param name="priority">The priority filter.</param>
    /// <param name="state">The execution state filter ("running" or "waiting").</param>
    public async Task<IActionResult> OnGetAsync(string? q, string? priority, string? state)
    {
        if (TempData[FeedbackKey] is string json)
            Feedback = JsonSerializer.Deserialize<ActionFeedback>(json);

        await LoadQueueAsync(q, priority, state);
        return Page();
    }

    /// <summary>
    /// Cancels a queued module scan by module ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module whose scan should be cancelled.</param>
    /// <param name="moduleName">The display name of the module whose scan is cancelled.</param>
    /// <param name="q">Current search filter.</param>
    /// <param name="priority">Current priority filter.</param>
    /// <param name="state">Current state filter.</param>
    public async Task<IActionResult> OnPostCancelAsync(Guid moduleId, string? moduleName, string? q, string? priority, string? state)
    {
        try
        {
            _logger.LogInformation("Cancelling scan for module {ModuleId}.", moduleId);
            await _modulixApiClient.ModuleManagement.CancelModuleScanAsync(
                moduleId,
                ct: HttpContext.RequestAborted);

            var targetName = !string.IsNullOrWhiteSpace(moduleName) ? moduleName : moduleId.ToString();
            _userActivityService.Record(
                User,
                "Cancelled scan for",
                targetName,
                "x-circle",
                UserActivityTone.Warning,
                Url.Page("/ScanQueuePage/Index"));

            TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                204,
                "Scan cancelled",
                $"Cancelled the queued scan for module {targetName}.",
                IsSuccess: true));
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Error cancelling scan for module {ModuleId}.", moduleId);
            var statusCode = (int?)exception.StatusCode ?? 503;
            var description = exception.StatusCode.HasValue
                ? exception.Message
                : ReasonPhrases.GetReasonPhrase(statusCode);

            try
            {
                var problem = JsonSerializer.Deserialize<ProblemDetails>(description, JsonSerializerOptions.Web);
                if (!string.IsNullOrWhiteSpace(problem?.Detail))
                    description = problem.Detail;
            }
            catch (JsonException)
            {
            }

            TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                statusCode,
                ReasonPhrases.GetReasonPhrase(statusCode),
                description,
                IsSuccess: false));
        }

        return RedirectToPage(new { q, priority, state });
    }

    private async Task LoadQueueAsync(string? q, string? priority, string? state)
    {
        Query = q?.Trim() ?? "";
        PriorityFilter = priority is nameof(ScanPriority.Patch) or nameof(ScanPriority.Create) ? priority : "";
        StateFilter = state is "running" or "waiting" ? state : "";

        try
        {
            var items = await _modulixApiClient.ModuleManagement.GetScanQueueAsync(
                ct: HttpContext.RequestAborted);

            _logger.LogDebug("Successfully loaded {QueueCount} scan queue entries.", items.Count);

            TotalCount = items.Count;
            RunningCount = items.Count(item => item.IsRunning);
            WaitingCount = items.Count(item => !item.IsRunning);
            PatchPriorityCount = items.Count(item => item.Priority == ScanPriority.Patch);

            var indexed = items.Select((item, index) => new QueueEntryView(index + 1, item));

            QueueEntries = indexed.Where(entry =>
                $"{entry.Item.ModuleName} {entry.Item.ModuleId} {entry.Item.Priority}"
                    .Contains(Query, StringComparison.OrdinalIgnoreCase)
                && (PriorityFilter.Length == 0 || entry.Item.Priority.ToString().Equals(PriorityFilter, StringComparison.OrdinalIgnoreCase))
                && (StateFilter.Length == 0
                    || (StateFilter == "running" && entry.Item.IsRunning)
                    || (StateFilter == "waiting" && !entry.Item.IsRunning)))
                .ToList();
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "ERROR loading scan queue.");
            var message = exception.StatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    "401 Unauthorized: The API rejected your access token. Please sign in again.",
                HttpStatusCode.InternalServerError =>
                    "500 Internal Server Error: The API could not load the scan queue. Please try again later.",
                HttpStatusCode statusCode when (int)statusCode >= 500 =>
                    $"API server error (HTTP {(int)statusCode}). Please try again later.",
                HttpStatusCode statusCode =>
                    $"Failed to load scan queue (HTTP {(int)statusCode}).",
                _ => "The API could not be reached. Please try again later."
            };
            ModelState.AddModelError(string.Empty, message);
        }
    }

    /// <summary>
    /// Represents a scan queue item with its 1-based queue position.
    /// </summary>
    public sealed record QueueEntryView(int Position, ModuleScanQueueItemDto Item);

    /// <summary>
    /// Represents feedback from a scan queue action.
    /// </summary>
    public sealed record ActionFeedback(int StatusCode, string Title, string Description, bool IsSuccess);
}
