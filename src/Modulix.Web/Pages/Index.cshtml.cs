using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Modulix.Client;
using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;
using Modulix.Client.Models.Enums;
using Modulix.Web.Services;

namespace Modulix.Web.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PageModel
{
    private const int RecentModulesLimit = 6;

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
    /// Gets the recorded user activities for the current login session.
    /// </summary>
    public IReadOnlyList<UserActivityItem> Activities { get; private set; } = [];

    /// <summary>
    /// Gets the signed-in user's first name, if available.
    /// </summary>
    public string? GivenName { get; private set; }

    /// <summary>
    /// Gets the signed-in user's last name, if available.
    /// </summary>
    public string? FamilyName { get; private set; }

    /// <summary>
    /// Gets the display name (first + last name, or fallback username) of the signed-in user.
    /// </summary>
    public string? UserFullName { get; private set; }

    /// <summary>
    /// Gets the initials for the signed-in user.
    /// </summary>
    public string UserInitials { get; private set; } = "U";

    /// <summary>
    /// Gets the list of all modules.
    /// </summary>
    public List<ModuleDto> Modules { get; private set; } = [];

    /// <summary>
    /// Gets the most recently created modules for the dashboard overview.
    /// </summary>
    public IReadOnlyList<ModuleDto> RecentModules { get; private set; } = [];

    /// <summary>
    /// Gets the modules that are currently in a failed state.
    /// </summary>
    public IReadOnlyList<ModuleDto> FailedModules { get; private set; } = [];

    /// <summary>
    /// Gets the modules awaiting endpoint confirmation.
    /// </summary>
    public IReadOnlyList<ModuleDto> PendingConfirmationModules { get; private set; } = [];

    /// <summary>
    /// Gets the current scan queue items.
    /// </summary>
    public List<ModuleScanQueueItemDto> ScanQueue { get; private set; } = [];

    /// <summary>
    /// Gets the error encountered while loading the scan queue, if any.
    /// </summary>
    public string? ScanQueueError { get; private set; }

    /// <summary>
    /// Gets all registered endpoints across modules.
    /// </summary>
    public List<ModuleSubEndpointsDetailDto> Endpoints { get; private set; } = [];

    /// <summary>
    /// Gets the error encountered while loading endpoints, if any.
    /// </summary>
    public string? EndpointsError { get; private set; }

    /// <summary>
    /// Gets the number of running modules.
    /// </summary>
    public int RunningModulesCount => Modules.Count(m => m.Status == ModuleStatus.Running);

    /// <summary>
    /// Gets the number of active endpoints.
    /// </summary>
    public int ActiveEndpointsCount => Endpoints.Count(e => e.Status == ModuleEndpointsStatus.Active);

    /// <summary>
    /// Gets the number of endpoints awaiting confirmation.
    /// </summary>
    public int PendingEndpointsCount => Endpoints.Count(e => e.Status == ModuleEndpointsStatus.PendingConfirmation);

    /// <summary>
    /// Gets the number of currently running scans.
    /// </summary>
    public int RunningScansCount => ScanQueue.Count(q => q.IsRunning);

    /// <summary>
    /// Gets the total number of items requiring attention.
    /// </summary>
    public int AttentionCount =>
        FailedModules.Count
        + PendingConfirmationModules.Count
        + ScanQueue.Count
        + (PendingEndpointsCount > 0 && PendingConfirmationModules.Count == 0 ? 1 : 0);

    /// <summary>
    /// Handles the GET request for the index page.
    /// </summary>
    /// <returns>An <see cref="IActionResult"/> representing the result of the request.</returns>
    public async Task<IActionResult> OnGetAsync()
    {
        ResolveUserIdentity();
        Activities = _userActivityService.GetActivities(User);

        var ct = HttpContext.RequestAborted;
        var modulesTask = LoadModulesAsync(ct);
        var queueTask = LoadScanQueueAsync(ct);
        var endpointsTask = LoadEndpointsAsync(ct);

        await Task.WhenAll(modulesTask, queueTask, endpointsTask);

        RecentModules = Modules
            .OrderByDescending(m => m.CreatedAtUtc)
            .ThenBy(m => m.ModuleName, StringComparer.OrdinalIgnoreCase)
            .Take(RecentModulesLimit)
            .ToList();

        FailedModules = Modules
            .Where(m => m.Status == ModuleStatus.Failed)
            .OrderBy(m => m.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PendingConfirmationModules = Modules
            .Where(m => m.Status == ModuleStatus.PendingConfirmation)
            .OrderBy(m => m.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Page();
    }

    private void ResolveUserIdentity()
    {
        var identity = UserDisplayIdentity.FromPrincipal(User);
        GivenName = identity.GivenName;
        FamilyName = identity.FamilyName;
        UserFullName = identity.FullName;
        UserInitials = identity.Initials;
    }

    private async Task LoadModulesAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Loading all modules for dashboard.");

            using var response = await _modulixApiClient.ModuleManagement.GetAllModulesAsync(
                pagination: new PaginationQueryDto { Page = 1, PageSize = PaginationQueryDto.MaxPageSize },
                ct: ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(ct);
                ModelState.AddModelError(string.Empty, error.DisplayMessage);
                return;
            }

            var pagedResult = await response.Content.ReadFromJsonAsync<PagedResultDto<ModuleDto>>(cancellationToken: ct);
            if (pagedResult is null)
            {
                _logger.LogWarning("The API returned an empty module response for the dashboard.");
                ModelState.AddModelError(string.Empty, "The API returned an empty module response.");
                return;
            }
            if (pagedResult.Items is null)
            {
                _logger.LogWarning("The API returned a module response without items for the dashboard.");
                ModelState.AddModelError(string.Empty, "The API returned an invalid module response.");
                return;
            }

            Modules = pagedResult.Items.ToList();

            _logger.LogDebug("Successfully loaded {ModuleCount} modules.", Modules.Count);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "ERROR loading modules.");
            ModelState.AddModelError(string.Empty, "The API could not be reached. Please try again later.");
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The API returned an invalid module response for the dashboard.");
            ModelState.AddModelError(string.Empty, "The API returned an invalid module response.");
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Loading modules for the dashboard timed out.");
            ModelState.AddModelError(string.Empty, "The API did not respond in time.");
        }
    }

    private async Task LoadScanQueueAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _modulixApiClient.ScannerManagement.GetScanQueueAsync(ct: ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(ct);
                _logger.LogWarning("Loading the scan queue for the dashboard returned HTTP {StatusCode}.", error.StatusCode);
                ScanQueueError = error.DisplayMessage;
                return;
            }

            var queue = await response.Content.ReadFromJsonAsync<List<ModuleScanQueueItemDto>>(cancellationToken: ct);
            if (queue is null)
            {
                _logger.LogWarning("The API returned an empty scan queue response for the dashboard.");
                ScanQueueError = "The API returned an empty scan queue response.";
                return;
            }

            ScanQueue = queue;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Could not load scan queue for dashboard.");
            ScanQueueError = "The API could not be reached. Please try again later.";
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The API returned an invalid scan queue response for the dashboard.");
            ScanQueueError = "The API returned an invalid scan queue response.";
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Loading the scan queue for the dashboard timed out.");
            ScanQueueError = "The API did not respond in time.";
        }
    }

    private async Task LoadEndpointsAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _modulixApiClient.SubEndpointManagement.GetAllEndpointsDetailAsync(
                pagination: new PaginationQueryDto { Page = 1, PageSize = PaginationQueryDto.MaxPageSize },
                ct: ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(ct);
                _logger.LogWarning("Loading endpoints for the dashboard returned HTTP {StatusCode}.", error.StatusCode);
                EndpointsError = error.DisplayMessage;
                return;
            }

            var pagedResult = await response.Content.ReadFromJsonAsync<PagedResultDto<ModuleSubEndpointsDetailDto>>(cancellationToken: ct);
            if (pagedResult is null)
            {
                _logger.LogWarning("The API returned an empty endpoint response for the dashboard.");
                EndpointsError = "The API returned an empty endpoint response.";
                return;
            }
            if (pagedResult.Items is null)
            {
                _logger.LogWarning("The API returned an endpoint response without items for the dashboard.");
                EndpointsError = "The API returned an invalid endpoint response.";
                return;
            }

            Endpoints = pagedResult.Items.ToList();
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Could not load endpoints for dashboard.");
            EndpointsError = "The API could not be reached. Please try again later.";
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The API returned an invalid endpoint response for the dashboard.");
            EndpointsError = "The API returned an invalid endpoint response.";
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Loading endpoints for the dashboard timed out.");
            EndpointsError = "The API did not respond in time.";
        }
    }
}
