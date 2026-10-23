using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;
using Modulix.Web.Services;

namespace Modulix.Web.Pages.ModulesPage;

/// <summary>
/// Module version update, rollback, and endpoint confirmation handlers for the module detail page.
/// </summary>
public partial class DetailModel
{
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
                HttpMethod = new HttpMethod(d.HttpMethod.Trim().ToUpperInvariant()),
                EndpointPath = d.EndpointPath.Trim()
            })
            .ToList();

        var removeEndpoints = validDecisions
            .Where(d => string.Equals(d.Action, "remove", StringComparison.OrdinalIgnoreCase))
            .Select(d => new DiscoveredSubEndpointDto
            {
                HttpMethod = new HttpMethod(d.HttpMethod.Trim().ToUpperInvariant()),
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
}
