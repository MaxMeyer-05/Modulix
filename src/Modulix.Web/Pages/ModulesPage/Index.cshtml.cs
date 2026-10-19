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
/// Handles the display and management of the modules index page.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PageModel
{
    /// <summary>
    /// The key used to store the result of a module creation in TempData.
    /// </summary>
    private const string ResultKey = "ModuleCreationResult";

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
    /// The list of modules that match the current search and filters.
    /// </summary>
    public List<ModuleDto> Modules { get; private set; } = [];

    /// <summary>
    /// The total number of modules before filtering.
    /// </summary>
    public int TotalModules { get; private set; }

    /// <summary>
    /// The number of currently running modules.
    /// </summary>
    public int RunningCount { get; private set; }

    /// <summary>
    /// The number of stopped modules.
    /// </summary>
    public int StoppedCount { get; private set; }

    /// <summary>
    /// The number of modules awaiting endpoint confirmation.
    /// </summary>
    public int PendingConfirmationCount { get; private set; }

    /// <summary>
    /// The number of modules queued for scanning.
    /// </summary>
    public int QueuedForScanCount { get; private set; }

    /// <summary>
    /// The number of modules currently starting or created.
    /// </summary>
    public int StartingCount { get; private set; }

    /// <summary>
    /// The number of modules in a failed state.
    /// </summary>
    public int FailedCount { get; private set; }

    /// <summary>
    /// The current search query.
    /// </summary>
    public string Query { get; private set; } = "";

    /// <summary>
    /// The current status filter.
    /// </summary>
    public string StatusFilter { get; private set; } = "";

    /// <summary>
    /// The current sort order.
    /// </summary>
    public string SortBy { get; private set; } = "newest";

    /// <summary>
    /// The submitted form values when the creation failed, so the form can be shown again.
    /// </summary>
    public CreateModuleDto? Input { get; private set; }

    /// <summary>
    /// The outcome of a rejected module creation.
    /// </summary>
    public CreationResult? CreationError { get; private set; }

    /// <summary>
    /// The outcome of an accepted module creation.
    /// </summary>
    public CreationResult? CreationSuccess { get; private set; }

    /// <summary>
    /// Feedback from a completed module action (such as deleting a module).
    /// </summary>
    public DetailModel.ActionFeedback? Feedback { get; private set; }

    /// <summary>
    /// Handles the GET request for the index page.
    /// </summary>
    /// <param name="q">The search text.</param>
    /// <param name="status">The status to filter by.</param>
    /// <param name="sort">The sort order.</param>
    /// <returns>An <see cref="IActionResult"/> representing the result of the request.</returns>
    public async Task<IActionResult> OnGetAsync(string? q, string? status, string? sort)
    {
        if (TempData[ResultKey] is string json)
            CreationSuccess = JsonSerializer.Deserialize<CreationResult>(json);

        if (TempData[DetailModel.FeedbackKey] is string feedbackJson)
            Feedback = JsonSerializer.Deserialize<DetailModel.ActionFeedback>(feedbackJson);

        await LoadModulesAsync(q, status, sort);
        return Page();
    }

    /// <summary>
    /// Loads the modules based on the current search and filter criteria.
    /// </summary>
    /// <param name="q">The search text.</param>
    /// <param name="status">The status to filter by.</param>
    /// <param name="sort">The sort order.</param>
    public async Task<IActionResult> OnPostAsync([FromForm] CreateModuleDto input)
    {
        var result = await CreateAsync(input);
        if (result.IsSuccess)
        {
            TempData[ResultKey] = JsonSerializer.Serialize(result);
            return RedirectToPage();
        }

        Input = input;
        CreationError = result;
        await LoadModulesAsync(null, null, null);
        return Page();
    }

    /// <summary>
    /// Creates a new module using the provided input.
    /// </summary>
    /// <param name="input">The input data for the new module.</param>
    private async Task<CreationResult> CreateAsync(CreateModuleDto input)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(value => value.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "A form field has an invalid value." : error.ErrorMessage);
            ModelState.Clear();
            return Failure(400, string.Join(" ", errors));
        }

        if (!input.ModuleFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return Failure(400, "Choose a ZIP archive.");

        try
        {
            var sanitizedModuleName = input.ModuleName
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);
            _logger.LogDebug("Creating a new module with name {ModuleName}.", sanitizedModuleName);

            using var response = await _modulixApiClient.ModuleManagement.CreateModuleAsync(
                input, ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
                return new CreationResult(error.StatusCode, error.Title, error.Detail, null);
            }

            var result = await response.Content.ReadFromJsonAsync<ModuleCreationResultDto>(
                cancellationToken: HttpContext.RequestAborted)
                ?? throw new JsonException("The API returned an empty response.");

            var module = result.Module;
            var statusCode = (int)response.StatusCode;
            var moduleUrl = statusCode is 201 or 202
                ? Url.Page("/ModulesPage/Detail", new { id = module.Id }) : null;
            var description = !string.IsNullOrWhiteSpace(result.Message)
                ? result.Message
                : ReasonPhrases.GetReasonPhrase(statusCode);

            _logger.LogInformation("Module creation returned HTTP {StatusCode} for {ModuleId}.", statusCode, module.Id);

            if (statusCode is 201 or 202)
            {
                var action = statusCode == 202 ? "Queued module scan for" : "Added";
                var tone = statusCode == 202 ? UserActivityTone.Update : UserActivityTone.Success;
                _userActivityService.Record(User, action, module.ModuleName, "plus", tone, moduleUrl);
            }

            return new CreationResult(statusCode, ReasonPhrases.GetReasonPhrase(statusCode), description, moduleUrl);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Error creating module.");
            return Failure(503, "The API could not be reached. Please try again later.");
        }
        catch (OperationCanceledException exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(exception, "Module creation timed out.");
            return Failure(504, "The API did not respond in time. Check the module list before retrying.");
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Invalid module creation response.");
            return Failure(502, "The API returned an invalid response. Check the module list before retrying.");
        }
    }

    /// <summary>
    /// Creates a failure result for a module creation attempt.
    /// </summary>
    /// <param name="statusCode">The HTTP status code of the failure.</param>
    /// <param name="description">The description of the failure.</param>
    /// <returns>A <see cref="CreationResult"/> representing the failure.</returns>
    private static CreationResult Failure(int statusCode, string description) =>
        new(statusCode, ReasonPhrases.GetReasonPhrase(statusCode), description, null);

    /// <summary>
    /// Loads the modules based on the current search and filter criteria.
    /// </summary>
    /// <param name="q">The search text.</param>
    /// <param name="status">The status to filter by.</param>
    /// <param name="sort">The sort order.</param>
    private async Task LoadModulesAsync(string? q, string? status, string? sort)
    {
        Query = q?.Trim() ?? "";
        StatusFilter = status ?? "";
        SortBy = sort is "name" or "name-desc" ? sort : "newest";

        try
        {
            using var response = await _modulixApiClient.ModuleManagement.GetAllModulesAsync(
                ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
                ModelState.AddModelError(string.Empty, error.DisplayMessage);
                return;
            }

            var modules = await response.Content.ReadFromJsonAsync<List<ModuleDto>>(
                cancellationToken: HttpContext.RequestAborted) ?? [];
            _logger.LogDebug("Successfully loaded {ModuleCount} modules.", modules.Count);

            TotalModules = modules.Count;
            RunningCount = modules.Count(m => m.Status == ModuleStatus.Running);
            StoppedCount = modules.Count(m => m.Status == ModuleStatus.Stopped);
            PendingConfirmationCount = modules.Count(m => m.Status == ModuleStatus.PendingConfirmation);
            QueuedForScanCount = modules.Count(m => m.Status == ModuleStatus.QueuedForScan);
            StartingCount = modules.Count(m => m.Status is ModuleStatus.Starting or ModuleStatus.Created);
            FailedCount = modules.Count(m => m.Status == ModuleStatus.Failed);

            var filtered = modules.Where(module =>
                $"{module.ModuleName} {module.Description} {module.BaseUrlPath} {module.Id}"
                    .Contains(Query, StringComparison.OrdinalIgnoreCase)
                && (StatusFilter.Length == 0 || module.Status.ToString() == StatusFilter));

            Modules = (SortBy switch
            {
                "name" => filtered.OrderBy(module => module.ModuleName, StringComparer.Create(CultureInfo.InvariantCulture, false)),
                "name-desc" => filtered.OrderByDescending(module => module.ModuleName, StringComparer.Create(CultureInfo.InvariantCulture, false)),
                _ => filtered.OrderByDescending(module => module.CreatedAtUtc)
            }).ToList();
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "ERROR loading modules.");
            ModelState.AddModelError(string.Empty, "The API could not be reached. Please try again later.");
        }
    }

    /// <summary>
    /// Represents the result of a module creation attempt.
    /// </summary>
    public sealed record CreationResult(int StatusCode, string Title, string Description, string? ModuleUrl)
    {
        /// <summary>
        /// Indicates whether the creation attempt was successful.
        /// </summary>
        public bool IsSuccess => StatusCode is 201 or 202;

        /// <summary>
        /// Gets the kind of the creation result, which can be "accepted", "success", or "error".
        /// </summary>
        public string Kind => StatusCode == 202 ? "accepted" : IsSuccess ? "success" : "error";
    }
}
