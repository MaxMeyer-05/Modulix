using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

using Modulix.Web.Services;

namespace Modulix.Web.Pages.ModulesPage;

/// <summary>
/// Container log loading and download handlers for the module detail page.
/// </summary>
public partial class DetailModel
{
    /// <summary>
    /// Downloads the container logs for the specified module as a plain-text file.
    /// </summary>
    /// <param name="id">The ID of the module whose container logs should be downloaded.</param>
    public async Task<IActionResult> OnGetDownloadLogsAsync(Guid id)
    {
        try
        {
            using var response = await _modulixApiClient.ModuleManagement.GetModuleContainerLogsAsync(
                moduleId: id,
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

            var logs = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            var fileName = $"module-{id:N}-container.log";
            return File(Encoding.UTF8.GetBytes(logs), "text/plain; charset=utf-8", fileName);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Error downloading container logs for module {ModuleId}.", id);
            TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                503,
                ReasonPhrases.GetReasonPhrase(503),
                "The API could not be reached. Please try again later.",
                IsSuccess: false));
            return RedirectToPage(new { id });
        }
        catch (OperationCanceledException exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(exception, "Downloading container logs for module {ModuleId} timed out.", id);
            TempData[FeedbackKey] = JsonSerializer.Serialize(new ActionFeedback(
                504,
                ReasonPhrases.GetReasonPhrase(504),
                "The API did not respond in time. Please try again.",
                IsSuccess: false));
            return RedirectToPage(new { id });
        }
    }

    /// <summary>
    /// Loads the container logs for the specified module when a container ID is assigned.
    /// </summary>
    /// <param name="id">The ID of the module.</param>
    private async Task LoadContainerLogsAsync(Guid id)
    {
        if (Module is null || string.IsNullOrWhiteSpace(Module.ContainerId))
            return;

        try
        {
            using var logsResponse = await _modulixApiClient.ModuleManagement.GetModuleContainerLogsAsync(
                moduleId: id,
                tail: LogsTail,
                ct: HttpContext.RequestAborted);

            if (!logsResponse.IsSuccessStatusCode)
            {
                var error = await logsResponse.ReadApiErrorAsync(HttpContext.RequestAborted);
                ContainerLogsError = error.DisplayMessage;
                return;
            }

            ContainerLogs = await logsResponse.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            ContainerLogLineCount = CountLogLines(ContainerLogs);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Error loading container logs for module {ModuleId}.", id);
            ContainerLogsError = "The container logs could not be retrieved from the API.";
        }
        catch (OperationCanceledException exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(exception, "Loading container logs for module {ModuleId} timed out.", id);
            ContainerLogsError = "Timed out while retrieving container logs.";
        }
    }

    /// <summary>
    /// Counts the number of log lines in the specified log text.
    /// </summary>
    private static int CountLogLines(string? logs)
    {
        if (string.IsNullOrWhiteSpace(logs))
            return 0;

        return logs.AsSpan().TrimEnd("\r\n").Count('\n') + 1;
    }
}
