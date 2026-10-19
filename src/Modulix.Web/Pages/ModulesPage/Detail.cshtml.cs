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
    /// Gets the submitted edit form values when an update failed.
    /// </summary>
    public UpdateModuleDto? EditInput { get; private set; }

    /// <summary>
    /// Gets the error from a failed metadata update attempt.
    /// </summary>
    public ActionError? EditError { get; private set; }

    /// <summary>
    /// Gets the error from a failed module file update attempt.
    /// </summary>
    public ActionError? UpdateFilesError { get; private set; }

    /// <summary>
    /// Gets the error from a failed module deletion attempt.
    /// </summary>
    public ActionError? DeleteError { get; private set; }

    /// <summary>
    /// Gets the feedback from the most recent completed module action.
    /// </summary>
    public ActionFeedback? Feedback { get; private set; }

    /// <summary>
    /// Loads the module detail for the specified module ID.
    /// </summary>
    /// <param name="id">The ID of the module to load.</param>
    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (TempData[FeedbackKey] is string json)
            Feedback = JsonSerializer.Deserialize<ActionFeedback>(json);

        await LoadModuleAsync(id);
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
    /// Uploads a replacement ZIP archive to patch the module's files.
    /// </summary>
    /// <param name="id">The ID of the module to patch.</param>
    /// <param name="input">The uploaded archive file.</param>
    public async Task<IActionResult> OnPostUpdateFilesAsync(Guid id, [FromForm] UpdateModuleFilesDto input)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(value => value.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "A form field has an invalid value." : error.ErrorMessage);
            ModelState.Clear();
            UpdateFilesError = Failure(400, string.Join(" ", errors));
            await LoadModuleAsync(id);
            return Page();
        }

        if (input.ModuleFile is null || !input.ModuleFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            UpdateFilesError = Failure(400, "Choose a ZIP archive.");
            await LoadModuleAsync(id);
            return Page();
        }

        try
        {
            _logger.LogInformation("Updating files for module {ModuleId}.", id);

            using var response = await _modulixApiClient.ModuleManagement.UpdateModuleFilesAsync(
                id,
                input,
                ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
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
            UpdateFilesError = Failure(503, "The API could not be reached. Please try again later.");
        }
        catch (OperationCanceledException exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(exception, "Updating files for module {ModuleId} timed out.", id);
            UpdateFilesError = Failure(504, "The API did not respond in time. Check the module status before retrying.");
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Invalid response while updating files for module {ModuleId}.", id);
            UpdateFilesError = Failure(502, "The API returned an invalid response. Check the module status before retrying.");
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

    private async Task LoadModuleAsync(Guid id)
    {
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
}