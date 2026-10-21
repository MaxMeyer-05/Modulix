using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

using Modulix.Client;
using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;
using Modulix.Web.Services;

namespace Modulix.Web.Pages.ModulesPage;

/// <summary>
/// Handles the display and management of module details.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DetailModel : PageModel
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
    /// Allowed page size options for the module endpoints list.
    /// </summary>
    public static readonly int[] AllowedPageSizes = [5, 10, 20, 50];

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
    private readonly ILogger<DetailModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DetailModel"/> class.
    /// </summary>
    /// <param name="modulixApiClient">The Modulix API client.</param>
    /// <param name="userActivityService">The user session activity service.</param>
    /// <param name="logger">The logger instance.</param>
    public DetailModel(
        IModulixApi modulixApiClient,
        IUserActivityService userActivityService,
        ILogger<DetailModel> logger)
    {
        _modulixApiClient = modulixApiClient;
        _userActivityService = userActivityService;
        _logger = logger;
    }

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
    /// Builds a URL for the module detail page preserving pagination parameters.
    /// </summary>
    public string GetPageUrl(Guid id, int targetPage)
    {
        var baseUrl = Url.Page("/ModulesPage/Detail", new { id }) ?? $"/modules/{id}";
        var queryParams = new Dictionary<string, string?>();

        if (targetPage > 1)
            queryParams["page"] = targetPage.ToString(CultureInfo.InvariantCulture);
        if (PageSize != DefaultEndpointsPageSize)
            queryParams["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture);

        return QueryHelpers.AddQueryString(baseUrl, queryParams);
    }

    /// <summary>
    /// Loads the module detail for the specified module ID.
    /// </summary>
    /// <param name="id">The ID of the module to load.</param>
    /// <param name="pageNumber">The 1-based page number for the module's endpoints.</param>
    /// <param name="pageSize">The number of endpoints per page.</param>
    public async Task<IActionResult> OnGetAsync(
        Guid id,
        [FromQuery(Name = "page")] int pageNumber = PaginationQueryDto.DefaultPage,
        [FromQuery(Name = "pageSize")] int pageSize = DefaultEndpointsPageSize)
    {
        if (TempData[FeedbackKey] is string json)
            Feedback = JsonSerializer.Deserialize<ActionFeedback>(json);

        await LoadModuleAsync(id, pageNumber, pageSize);
        return Page();
    }

    /// <summary>
    /// Starts the module container.
    /// </summary>
    /// <param name="id">The ID of the module to start.</param>
    /// <param name="moduleName">The display name of the module.</param>
    public async Task<IActionResult> OnPostStartAsync(Guid id, [FromForm] string? moduleName)
    {
        ModelState.Clear();

        try
        {
            _logger.LogInformation("Starting module {ModuleId}.", id);

            using var response = await _modulixApiClient.ModuleManagement.StartModuleAsync(
                id,
                ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
                TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                    error.StatusCode,
                    error.Title,
                    error.Detail,
                    IsSuccess: false));
                return RedirectToPage(new { id });
            }

            var updatedModule = await response.Content.ReadFromJsonAsync<ModuleDetailDto>(
                cancellationToken: HttpContext.RequestAborted);
            var targetName = updatedModule?.ModuleName ?? (!string.IsNullOrWhiteSpace(moduleName) ? moduleName.Trim() : id.ToString());
            var detailUrl = Url.Page("/ModulesPage/Detail", new { id });

            if (updatedModule?.Status == ModuleStatus.Failed)
            {
                _userActivityService.Record(User, "Failed starting", targetName, "triangle-alert", UserActivityTone.Danger, detailUrl);
                TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                    500,
                    "Failed to start module",
                    $"The container for {targetName} failed to start.",
                    IsSuccess: false));
            }
            else
            {
                _userActivityService.Record(User, "Started", targetName, "play", UserActivityTone.Success, detailUrl);
                TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                    (int)response.StatusCode,
                    "Module started",
                    $"Started module {targetName}.",
                    IsSuccess: true));
            }
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Error starting module {ModuleId}.", id);
            TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                503,
                ReasonPhrases.GetReasonPhrase(503),
                "The API could not be reached. Please try again later.",
                IsSuccess: false));
        }
        catch (OperationCanceledException exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(exception, "Starting module {ModuleId} timed out.", id);
            TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                504,
                ReasonPhrases.GetReasonPhrase(504),
                "The API did not respond in time. Check the module status before retrying.",
                IsSuccess: false));
        }

        return RedirectToPage(new { id });
    }

    /// <summary>
    /// Stops the running module container.
    /// </summary>
    /// <param name="id">The ID of the module to stop.</param>
    /// <param name="moduleName">The display name of the module.</param>
    public async Task<IActionResult> OnPostStopAsync(Guid id, [FromForm] string? moduleName)
    {
        ModelState.Clear();

        try
        {
            _logger.LogInformation("Stopping module {ModuleId}.", id);

            using var response = await _modulixApiClient.ModuleManagement.StopModuleAsync(
                id,
                ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
                TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                    error.StatusCode,
                    error.Title,
                    error.Detail,
                    IsSuccess: false));
                return RedirectToPage(new { id });
            }

            var updatedModule = await response.Content.ReadFromJsonAsync<ModuleDetailDto>(
                cancellationToken: HttpContext.RequestAborted);
            var targetName = updatedModule?.ModuleName ?? (!string.IsNullOrWhiteSpace(moduleName) ? moduleName.Trim() : id.ToString());

            _userActivityService.Record(
                User,
                "Stopped",
                targetName,
                "square",
                UserActivityTone.Warning,
                Url.Page("/ModulesPage/Detail", new { id }));

            TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                (int)response.StatusCode,
                "Module stopped",
                $"Stopped module {targetName}.",
                IsSuccess: true));
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Error stopping module {ModuleId}.", id);
            TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                503,
                ReasonPhrases.GetReasonPhrase(503),
                "The API could not be reached. Please try again later.",
                IsSuccess: false));
        }
        catch (OperationCanceledException exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(exception, "Stopping module {ModuleId} timed out.", id);
            TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                504,
                ReasonPhrases.GetReasonPhrase(504),
                "The API did not respond in time. Check the module status before retrying.",
                IsSuccess: false));
        }

        return RedirectToPage(new { id });
    }

    /// <summary>
    /// Updates the name and description of the module.
    /// </summary>
    /// <param name="id">The ID of the module to update.</param>
    /// <param name="input">The updated module metadata.</param>
    public async Task<IActionResult> OnPostEditAsync(Guid id, [FromForm] UpdateModuleDto input)
    {
        if (string.IsNullOrWhiteSpace(input.ModuleName))
            ModelState.AddModelError(nameof(input.ModuleName), "Module name is required.");

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(value => value.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "A form field has an invalid value." : error.ErrorMessage);
            ModelState.Clear();
            EditInput = input;
            EditError = Failure(400, string.Join(" ", errors));
            await LoadModuleAsync(id);
            return Page();
        }

        var trimmedName = input.ModuleName!.Trim();
        var request = new UpdateModuleDto
        {
            ModuleName = trimmedName,
            Description = input.Description?.Trim() ?? string.Empty
        };

        try
        {
            var sanitizedName = trimmedName.Replace("\r", string.Empty).Replace("\n", string.Empty);
            _logger.LogInformation("Updating details for module {ModuleId} ({ModuleName}).", id, sanitizedName);

            using var response = await _modulixApiClient.ModuleManagement.UpdateModuleAsync(
                id,
                request,
                ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
                EditInput = input;
                EditError = new ActionError(error.StatusCode, error.Title, error.Detail);
                await LoadModuleAsync(id);
                return Page();
            }

            _userActivityService.Record(
                User,
                "Updated details for",
                trimmedName,
                "pencil",
                UserActivityTone.Update,
                Url.Page("/ModulesPage/Detail", new { id }));

            TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                (int)response.StatusCode,
                "Module details updated",
                $"Saved updated details for {trimmedName}.",
                IsSuccess: true));

            return RedirectToPage(new { id });
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Error updating details for module {ModuleId}.", id);
            EditInput = input;
            EditError = Failure(503, "The API could not be reached. Please try again later.");
        }
        catch (OperationCanceledException exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(exception, "Updating module {ModuleId} timed out.", id);
            EditInput = input;
            EditError = Failure(504, "The API did not respond in time. Please try again.");
        }

        await LoadModuleAsync(id);
        return Page();
    }

    /// <summary>
    /// Uploads a replacement ZIP archive to deploy a new version of the module.
    /// </summary>
    /// <param name="id">The ID of the module to patch.</param>
    /// <param name="input">The new version metadata and uploaded archive file.</param>
    public async Task<IActionResult> OnPostUpdateFilesAsync(Guid id, [FromForm] ModuleVersionCreateDto input)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(value => value.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "A form field has an invalid value." : error.ErrorMessage);
            ModelState.Clear();
            UpdateFilesInput = input;
            UpdateFilesError = Failure(400, string.Join(" ", errors));
            await LoadModuleAsync(id);
            return Page();
        }

        if (input.ModuleFile is null || !input.ModuleFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            UpdateFilesInput = input;
            UpdateFilesError = Failure(400, "Choose a ZIP archive.");
            await LoadModuleAsync(id);
            return Page();
        }

        try
        {
            _logger.LogInformation("Creating new version for module {ModuleId}.", id);

            using var response = await _modulixApiClient.ModuleVersionManagement.CreateModuleVersionAsync(
                id,
                input,
                ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
                UpdateFilesInput = input;
                UpdateFilesError = new ActionError(error.StatusCode, error.Title, error.Detail);
                await LoadModuleAsync(id);
                return Page();
            }

            var result = await response.Content.ReadFromJsonAsync<ModuleCreationResultDto>(
                cancellationToken: HttpContext.RequestAborted)
                ?? throw new JsonException("The API returned an empty response.");

            var updatedModule = result.Module;
            var detailUrl = Url.Page("/ModulesPage/Detail", new { id });

            var (action, icon, tone, title, description, isSuccess, statusCode) = updatedModule.Status switch
            {
                ModuleStatus.QueuedForScan => (
                    "Queued patch scan for",
                    "upload",
                    UserActivityTone.Update,
                    "Patch scan queued",
                    !string.IsNullOrWhiteSpace(result.Message)
                        ? result.Message
                        : $"Queued high-priority scan for {updatedModule.ModuleName}.",
                    true,
                    202),
                ModuleStatus.PendingConfirmation => (
                    "Updated files for",
                    "upload",
                    UserActivityTone.Warning,
                    "Endpoints require confirmation",
                    !string.IsNullOrWhiteSpace(result.Message)
                        ? result.Message
                        : "Endpoint changes were detected and require confirmation before the updated container is activated.",
                    true,
                    200),
                ModuleStatus.Failed => (
                    "Failed patching",
                    "triangle-alert",
                    UserActivityTone.Danger,
                    "Replacement container failed",
                    "The uploaded archive was scanned, but the replacement container failed to start.",
                    false,
                    500),
                _ => (
                    "Updated files for",
                    "upload",
                    UserActivityTone.Success,
                    "Module files updated",
                    !string.IsNullOrWhiteSpace(result.Message)
                        ? result.Message
                        : $"Replacement container for {updatedModule.ModuleName} activated successfully.",
                    true,
                    200)
            };

            _userActivityService.Record(User, action, updatedModule.ModuleName, icon, tone, detailUrl);

            TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                statusCode,
                title,
                description,
                IsSuccess: isSuccess));

            return RedirectToPage(new { id });
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Error updating files for module {ModuleId}.", id);
            UpdateFilesInput = input;
            UpdateFilesError = Failure(503, "The API could not be reached. Please try again later.");
        }
        catch (OperationCanceledException exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(exception, "Updating files for module {ModuleId} timed out.", id);
            UpdateFilesInput = input;
            UpdateFilesError = Failure(504, "The API did not respond in time. Check the module status before retrying.");
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Invalid response while updating files for module {ModuleId}.", id);
            UpdateFilesInput = input;
            UpdateFilesError = Failure(502, "The API returned an invalid response. Check the module status before retrying.");
        }

        await LoadModuleAsync(id);
        return Page();
    }

    /// <summary>
    /// Rolls back the module to a specified earlier version.
    /// </summary>
    /// <param name="id">The ID of the module to roll back.</param>
    /// <param name="version">The target version string to roll back to.</param>
    /// <param name="moduleName">The display name of the module.</param>
    public async Task<IActionResult> OnPostRollbackAsync(Guid id, [FromForm] string? version, [FromForm] string? moduleName)
    {
        ModelState.Clear();

        if (string.IsNullOrWhiteSpace(version))
        {
            RollbackError = Failure(400, "Select a target version to roll back to.");
            await LoadModuleAsync(id);
            return Page();
        }

        var trimmedVersion = version.Trim();
        RollbackVersion = trimmedVersion;

        try
        {
            var sanitizedVersion = trimmedVersion.Replace("\r", string.Empty).Replace("\n", string.Empty);
            _logger.LogInformation("Rolling back module {ModuleId} to version {Version}.", id, sanitizedVersion);

            using var response = await _modulixApiClient.ModuleVersionManagement.RollbackModuleVersionAsync(
                id,
                trimmedVersion,
                ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
                RollbackError = new ActionError(error.StatusCode, error.Title, error.Detail);
                await LoadModuleAsync(id);
                return Page();
            }

            var updatedModule = await response.Content.ReadFromJsonAsync<ModuleDetailDto>(
                cancellationToken: HttpContext.RequestAborted);
            var targetName = updatedModule?.ModuleName ?? (!string.IsNullOrWhiteSpace(moduleName) ? moduleName.Trim() : id.ToString());
            var resolvedVersion = updatedModule?.CurrentVersion ?? trimmedVersion;
            var detailUrl = Url.Page("/ModulesPage/Detail", new { id });

            if (updatedModule?.Status == ModuleStatus.Failed)
            {
                _userActivityService.Record(User, "Failed rolling back", $"{targetName} ({resolvedVersion})", "triangle-alert", UserActivityTone.Danger, detailUrl);
                TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                    500,
                    "Rollback container failed",
                    $"Rolled back {targetName} to version {resolvedVersion}, but the container failed to start.",
                    IsSuccess: false));
            }
            else
            {
                _userActivityService.Record(User, "Rolled back", $"{targetName} to {resolvedVersion}", "rotate-ccw", UserActivityTone.Update, detailUrl);
                TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                    (int)response.StatusCode,
                    "Module rolled back",
                    $"Rolled back {targetName} to version {resolvedVersion}.",
                    IsSuccess: true));
            }

            return RedirectToPage(new { id });
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Error rolling back module {ModuleId}.", id);
            RollbackError = Failure(503, "The API could not be reached. Please try again later.");
        }
        catch (OperationCanceledException exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(exception, "Rolling back module {ModuleId} timed out.", id);
            RollbackError = Failure(504, "The API did not respond in time. Check the module status before retrying.");
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Invalid response while rolling back module {ModuleId}.", id);
            RollbackError = Failure(502, "The API returned an invalid response. Check the module status before retrying.");
        }

        await LoadModuleAsync(id);
        return Page();
    }

    /// <summary>
    /// Confirms or removes pending sub-endpoints for a module in PendingConfirmation status.
    /// </summary>
    /// <param name="id">The ID of the module.</param>
    /// <param name="decisions">The confirmation decisions for pending endpoints.</param>
    /// <param name="moduleName">The display name of the module.</param>
    public async Task<IActionResult> OnPostConfirmEndpointsAsync(
        Guid id,
        [FromForm] List<EndpointConfirmationDecision>? decisions,
        [FromForm] string? moduleName)
    {
        ModelState.Clear();

        var validDecisions = decisions?
            .Where(d => !string.IsNullOrWhiteSpace(d.HttpMethod) && !string.IsNullOrWhiteSpace(d.EndpointPath))
            .ToList() ?? [];

        if (validDecisions.Count == 0)
        {
            ConfirmEndpointsError = Failure(400, "No pending endpoints were selected for confirmation.");
            await LoadModuleAsync(id);
            return Page();
        }

        var activateEndpoints = validDecisions
            .Where(d => !string.Equals(d.Action, "remove", StringComparison.OrdinalIgnoreCase))
            .Select(d => new DiscoveredSubEndpointDto
            {
                HttpMethod = d.HttpMethod.Trim().ToUpperInvariant(),
                EndpointPath = d.EndpointPath.Trim()
            })
            .ToList();

        var removeEndpoints = validDecisions
            .Where(d => string.Equals(d.Action, "remove", StringComparison.OrdinalIgnoreCase))
            .Select(d => new DiscoveredSubEndpointDto
            {
                HttpMethod = d.HttpMethod.Trim().ToUpperInvariant(),
                EndpointPath = d.EndpointPath.Trim()
            })
            .ToList();

        var request = new ConfirmEndpointsDto
        {
            ConfirmedEndpoints =
            [
                new SubEndpointDiscrepancyReportDto
                {
                    ModuleId = id,
                    HasDiscrepancy = true,
                    ExtraEndpoints = activateEndpoints.Count > 0 ? activateEndpoints : null,
                    MissingEndpoints = removeEndpoints.Count > 0 ? removeEndpoints : null
                }
            ]
        };

        try
        {
            _logger.LogInformation(
                "Confirming endpoints for module {ModuleId} (Activate: {ActivateCount}, Remove: {RemoveCount}).",
                id,
                activateEndpoints.Count,
                removeEndpoints.Count);

            using var response = await _modulixApiClient.SubEndpointManagement.ConfirmEndpointAsync(
                id,
                request,
                ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
                ConfirmEndpointsError = new ActionError(error.StatusCode, error.Title, error.Detail);
                await LoadModuleAsync(id);
                return Page();
            }

            var updatedModule = await response.Content.ReadFromJsonAsync<ModuleDetailDto>(
                cancellationToken: HttpContext.RequestAborted);
            var targetName = updatedModule?.ModuleName ?? (!string.IsNullOrWhiteSpace(moduleName) ? moduleName.Trim() : id.ToString());
            var detailUrl = Url.Page("/ModulesPage/Detail", new { id });

            if (updatedModule?.Status == ModuleStatus.Failed)
            {
                _userActivityService.Record(User, "Failed starting confirmed", targetName, "triangle-alert", UserActivityTone.Danger, detailUrl);
                TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                    500,
                    "Container failed after confirmation",
                    $"Confirmed endpoints for {targetName}, but the module container failed to start.",
                    IsSuccess: false));
            }
            else if (updatedModule?.Status == ModuleStatus.PendingConfirmation)
            {
                _userActivityService.Record(User, "Updated pending endpoints for", targetName, "check-circle-2", UserActivityTone.Update, detailUrl);
                TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                    (int)response.StatusCode,
                    "Endpoint decision saved",
                    $"Updated endpoint status for {targetName}. Confirm remaining pending endpoints to start the module.",
                    IsSuccess: true));
            }
            else
            {
                _userActivityService.Record(User, "Confirmed endpoints for", targetName, "check-circle-2", UserActivityTone.Success, detailUrl);
                TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                    (int)response.StatusCode,
                    "Endpoints confirmed",
                    $"Confirmed endpoints and activated module {targetName}.",
                    IsSuccess: true));
            }

            return RedirectToPage(new { id });
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Error confirming endpoints for module {ModuleId}.", id);
            ConfirmEndpointsError = Failure(503, "The API could not be reached. Please try again later.");
        }
        catch (OperationCanceledException exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(exception, "Confirming endpoints for module {ModuleId} timed out.", id);
            ConfirmEndpointsError = Failure(504, "The API did not respond in time. Check the module status before retrying.");
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Invalid response while confirming endpoints for module {ModuleId}.", id);
            ConfirmEndpointsError = Failure(502, "The API returned an invalid response. Check the module status before retrying.");
        }

        await LoadModuleAsync(id);
        return Page();
    }

    /// <summary>
    /// Deletes the module, its container, storage directory, and registered endpoints.
    /// </summary>
    /// <param name="id">The ID of the module to delete.</param>
    /// <param name="moduleName">The display name of the module being deleted.</param>
    public async Task<IActionResult> OnPostDeleteAsync(Guid id, [FromForm] string? moduleName)
    {
        ModelState.Clear();

        try
        {
            _logger.LogInformation("Deleting module {ModuleId}.", id);

            using var response = await _modulixApiClient.ModuleManagement.DeleteModuleAsync(
                id,
                ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
                DeleteError = new ActionError(error.StatusCode, error.Title, error.Detail);
                await LoadModuleAsync(id);
                return Page();
            }

            var targetName = !string.IsNullOrWhiteSpace(moduleName) ? moduleName.Trim() : id.ToString();
            _userActivityService.Record(
                User,
                "Deleted",
                targetName,
                "trash-2",
                UserActivityTone.Danger,
                Url.Page("/ModulesPage/Index"));

            TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                (int)response.StatusCode,
                "Module deleted",
                $"Deleted module {targetName} and removed its container and files.",
                IsSuccess: true));

            return RedirectToPage("/ModulesPage/Index");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Error deleting module {ModuleId}.", id);
            DeleteError = Failure(503, "The API could not be reached. Please try again later.");
        }
        catch (OperationCanceledException exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(exception, "Deleting module {ModuleId} timed out.", id);
            DeleteError = Failure(504, "The API did not respond in time. Please try again.");
        }

        await LoadModuleAsync(id);
        return Page();
    }

    private async Task LoadModuleAsync(
        Guid id,
        int pageNumber = PaginationQueryDto.DefaultPage,
        int pageSize = DefaultEndpointsPageSize)
    {
        CurrentPage = pageNumber < 1 ? PaginationQueryDto.DefaultPage : pageNumber;
        PageSize = pageSize < 1
            ? DefaultEndpointsPageSize
            : Math.Min(pageSize, PaginationQueryDto.MaxPageSize);

        try
        {
            _logger.LogDebug("Loading module detail for ID {ModuleId}", id);

            using var response = await _modulixApiClient.ModuleManagement.GetModuleByIdAsync(
                moduleId: id,
                ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
                ModelState.AddModelError(string.Empty, error.DisplayMessage);
                return;
            }

            Module = await response.Content.ReadFromJsonAsync<ModuleDetailDto>(
                cancellationToken: HttpContext.RequestAborted);

            if (Module is null)
                return;

            using var endpointsResponse = await _modulixApiClient.SubEndpointManagement.GetModuleEndpointsAsync(
                moduleId: id,
                pagination: new PaginationQueryDto { Page = CurrentPage, PageSize = PageSize },
                ct: HttpContext.RequestAborted);

            if (endpointsResponse.IsSuccessStatusCode)
            {
                var pagedEndpoints = await endpointsResponse.Content.ReadFromJsonAsync<PagedResultDto<ModuleSubEndpointDto>>(
                    cancellationToken: HttpContext.RequestAborted);

                if (pagedEndpoints is not null)
                {
                    Endpoints = pagedEndpoints.Items;
                    TotalEndpoints = pagedEndpoints.TotalCount;
                    CurrentPage = pagedEndpoints.Page > 0 ? pagedEndpoints.Page : CurrentPage;
                    PageSize = pagedEndpoints.PageSize > 0 ? pagedEndpoints.PageSize : PageSize;
                    TotalPages = Math.Max(1, pagedEndpoints.TotalPages);
                    HasPreviousPage = pagedEndpoints.HasPreviousPage;
                    HasNextPage = pagedEndpoints.HasNextPage;
                    return;
                }
            }

            var fallbackEndpoints = Module.Endpoints.ToList();
            TotalEndpoints = fallbackEndpoints.Count;
            TotalPages = Math.Max(1, (int)Math.Ceiling((double)TotalEndpoints / PageSize));
            HasPreviousPage = CurrentPage > 1;
            HasNextPage = CurrentPage < TotalPages;
            Endpoints = fallbackEndpoints
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "ERROR loading module detail.");
            ModelState.AddModelError(string.Empty, "The API could not be reached. Please try again later.");
        }
    }

    private static ActionError Failure(int statusCode, string description) =>
        new(statusCode, ReasonPhrases.GetReasonPhrase(statusCode), description);

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