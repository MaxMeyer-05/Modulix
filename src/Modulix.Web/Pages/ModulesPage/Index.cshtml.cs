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
    /// Gets the list of modules.
    /// </summary>
    public List<ModuleDto> Modules { get; private set; } = [];

    /// <summary>
    /// Handles the GET request for the index page.
    /// </summary>
    /// <returns>An <see cref="IActionResult"/> representing the result of the request.</returns>
    public async Task<IActionResult> OnGetAsync()
    {        
        try
        {
            Modules = await _modulixApiClient.ModuleManagement.GetAllModulesAsync();
            _logger.LogDebug("Successfully loaded {ModuleCount} modules.", Modules.Count);
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

        return Page();
    }

    public async Task<IActionResult> OnPostAsync([FromForm] CreateModuleDto input)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(value => value.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "A form field has an invalid value." : error.ErrorMessage);
            return CreationResponse(400, string.Join(" ", errors));
        }

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

            if (statusCode == 201 && moduleUrl is not null)
                Response.Headers.Location = moduleUrl;
            return CreationResponse(statusCode, description, moduleUrl);
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
            return CreationResponse(statusCode, description);
        }
        catch (OperationCanceledException exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(exception, "Module creation timed out.");
            return CreationResponse(504, "The API did not respond in time. Check the module list before retrying.");
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Invalid module creation response.");
            return CreationResponse(502, "The API returned an invalid response. Check the module list before retrying.");
        }
    }

    private static JsonResult CreationResponse(int statusCode, string description, string? moduleUrl = null) =>
        new(new { statusCode, title = ReasonPhrases.GetReasonPhrase(statusCode), description, moduleUrl })
        {
            StatusCode = statusCode
        };
}