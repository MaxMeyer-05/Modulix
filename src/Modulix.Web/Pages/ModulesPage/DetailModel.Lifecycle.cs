using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;
using Modulix.Web.Services;

namespace Modulix.Web.Pages.ModulesPage;

/// <summary>
/// Module lifecycle and metadata handlers (Start, Stop, Edit, Delete) for the module detail page.
/// </summary>
public partial class DetailModel
{
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

            _endpointDiscrepancyService?.ClearReport(id);
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
}
