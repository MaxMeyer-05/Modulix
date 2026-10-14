using System.Globalization;
using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

using Modulix.Client;
using Modulix.Client.Models.Dtos;

namespace Modulix.Web.Pages.ModulesPage;

public class IndexModel : PageModel
{
    private const string ResultKey = "ModuleCreationResult";

    /// <summary>
    /// The Modulix API client.
    /// </summary>
    private readonly IModulixApi _modulixApiClient;

    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<IndexModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexModel"/> class.
    /// </summary>
    /// <param name="modulixApiClient">The Modulix API client.</param>
    /// <param name="logger">The logger instance.</param>
    public IndexModel(
        IModulixApi modulixApiClient, 
        ILogger<IndexModel> logger)
    {
        _modulixApiClient = modulixApiClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the modules that match the current search and filters.
    /// </summary>
    public List<ModuleDto> Modules { get; private set; } = [];

    /// <summary>
    /// Gets the total number of modules before filtering.
    /// </summary>
    public int TotalModules { get; private set; }

    public string Query { get; private set; } = "";

    public string StatusFilter { get; private set; } = "";

    public string SortBy { get; private set; } = "newest";

    /// <summary>
    /// Gets the submitted form values when the creation failed, so the form can be shown again.
    /// </summary>
    public CreateModuleDto? Input { get; private set; }

    /// <summary>
    /// Gets the outcome of a rejected module creation.
    /// </summary>
    public CreationResult? CreationError { get; private set; }

    /// <summary>
    /// Gets the outcome of an accepted module creation.
    /// </summary>
    public CreationResult? CreationSuccess { get; private set; }

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

        await LoadModulesAsync(q, status, sort);
        return Page();
    }

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
            _logger.LogDebug("Creating a new module with name {ModuleName}.", input.ModuleName);

            var response = await _modulixApiClient.ModuleManagement.CreateModuleWithStatusAsync(
                input, ct: HttpContext.RequestAborted);

            var module = response.Result.Module;
            var statusCode = (int)response.StatusCode;
            var moduleUrl = statusCode is 201 or 202
                ? Url.Page("/ModulesPage/Detail", new { id = module.Id }) : null;
            var description = !string.IsNullOrWhiteSpace(response.Result.Message)
                ? response.Result.Message
                : ReasonPhrases.GetReasonPhrase(statusCode);

            _logger.LogInformation("Module creation returned HTTP {StatusCode} for {ModuleId}.", statusCode, module.Id);

            return new CreationResult(statusCode, ReasonPhrases.GetReasonPhrase(statusCode), description, moduleUrl);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Error creating module.");
            var statusCode = (int?)exception.StatusCode ?? 503;
            var description = exception.StatusCode.HasValue
                ? exception.Message : ReasonPhrases.GetReasonPhrase(statusCode);
            try
            {
                var problem = JsonSerializer.Deserialize<ProblemDetails>(description, JsonSerializerOptions.Web);
                if (!string.IsNullOrWhiteSpace(problem?.Detail))
                    description = problem.Detail;
            }
            catch (JsonException)
            {
            }
            return Failure(statusCode, description);
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

    private static CreationResult Failure(int statusCode, string description) =>
        new(statusCode, ReasonPhrases.GetReasonPhrase(statusCode), description, null);

    private async Task LoadModulesAsync(string? q, string? status, string? sort)
    {
        Query = q?.Trim() ?? "";
        StatusFilter = status ?? "";
        SortBy = sort is "name" or "name-desc" ? sort : "newest";

        try
        {
            var modules = await _modulixApiClient.ModuleManagement.GetAllModulesAsync();
            _logger.LogDebug("Successfully loaded {ModuleCount} modules.", modules.Count);

            TotalModules = modules.Count;
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
            var message = exception.StatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    "401 Unauthorized: The API rejected your access token. Please sign in again.",
                HttpStatusCode.InternalServerError =>
                    "500 Internal Server Error: The API could not load the modules. Please try again later.",
                HttpStatusCode statusCode when (int)statusCode >= 500 =>
                    $"API server error (HTTP {(int)statusCode}). Please try again later.",
                HttpStatusCode statusCode =>
                    $"Failed to load modules (HTTP {(int)statusCode}).",
                _ => "The API could not be reached. Please try again later."
            };
            ModelState.AddModelError(string.Empty, message);
        }
    }

    public sealed record CreationResult(int StatusCode, string Title, string Description, string? ModuleUrl)
    {
        public bool IsSuccess => StatusCode is 201 or 202;

        public string Kind => StatusCode == 202 ? "accepted" : IsSuccess ? "success" : "error";
    }
}
