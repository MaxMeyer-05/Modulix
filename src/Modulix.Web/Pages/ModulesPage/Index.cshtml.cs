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
            _logger.LogDebug("Loading all modules.");

            Modules = await _modulixApiClient.ModuleManagement.GetAllModulesAsync(
                ct: HttpContext.RequestAborted);

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
            var description = response.StatusCode switch
            {
                HttpStatusCode.Created => $"'{module.ModuleName}' was created. Current status: {ModuleStatusDisplay.StatusLabel(module.Status)}.",
                HttpStatusCode.Accepted => $"'{module.ModuleName}' was accepted for processing. Current status: {ModuleStatusDisplay.StatusLabel(module.Status)}.",
                _ => "The API returned an unexpected success response."
            };
            if (!string.IsNullOrWhiteSpace(response.Result.Message))
                description += $" {response.Result.Message}";

            _logger.LogInformation("Module creation returned HTTP {StatusCode} for {ModuleId}.", statusCode, module.Id);
            
            if (statusCode == 201 && moduleUrl is not null)
                Response.Headers.Location = moduleUrl;
            return CreationResponse(statusCode, description, moduleUrl);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Error creating module.");
            var description = exception.StatusCode switch
            {
                HttpStatusCode.BadRequest => "The API rejected the module data or ZIP archive. Check the form and try again.",
                HttpStatusCode.Unauthorized => "Your access token was rejected. Please sign in again.",
                HttpStatusCode.Forbidden => "You do not have permission to create modules.",
                HttpStatusCode.NotFound => "The module creation endpoint could not be found.",
                HttpStatusCode.Conflict => "The module conflicts with an existing module. Check its name and base URL path.",
                HttpStatusCode.RequestEntityTooLarge => "The upload exceeds the server's size limit.",
                HttpStatusCode.UnsupportedMediaType => "The API rejected the upload format. A ZIP archive is required.",
                HttpStatusCode.UnprocessableEntity => "The API could not process the module data or archive.",
                HttpStatusCode.TooManyRequests => "Too many requests were sent. Please try again later.",
                HttpStatusCode statusCode when (int)statusCode >= 500 => "The API encountered a server error. Check the module list before retrying.",
                null => "The API could not be reached. No API response was received. Check the module list before retrying.",
                _ => "The API rejected module creation."
            };
            return CreationResponse((int?)exception.StatusCode ?? 503, description);
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