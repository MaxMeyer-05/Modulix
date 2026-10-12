using System.Net;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Modulix.Client;
using Modulix.Client.Models.Dtos;

namespace Modulix.Web.Pages.ModulesPage;

public class DetailModel : PageModel
{
    /// <summary>
    /// The Modulix API client.
    /// </summary>
    private readonly IModulixApi _modulixApiClient;

    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<DetailModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DetailModel"/> class.
    /// </summary>
    /// <param name="modulixApiClient">The Modulix API client.</param>
    /// <param name="logger">The logger instance.</param>
    public DetailModel(
        IModulixApi modulixApiClient, 
        ILogger<DetailModel> logger)
    {
        _modulixApiClient = modulixApiClient;
        _logger = logger;
    }
    
    /// <summary>
    /// Gets the module detail.
    /// </summary>
    public ModuleDetailDto? Module { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        try
        {
            _logger.LogDebug("Loading module detail for ID {ModuleId}", id);

            Module = await _modulixApiClient.ModuleManagement.GetModuleByIdAsync(
                moduleId: id,
                ct: HttpContext.RequestAborted);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "ERROR loading module detail.");
            var message = exception.StatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    "401 Unauthorized: The API rejected your access token. Please sign in again.",
                HttpStatusCode.NotFound =>
                    "404 Not Found: The requested module could not be found.",
                HttpStatusCode.InternalServerError =>
                    "500 Internal Server Error: The API could not load the module detail. Please try again later.",
                HttpStatusCode statusCode when (int)statusCode >= 500 =>
                    $"API server error (HTTP {(int)statusCode}). Please try again later.",
                HttpStatusCode statusCode =>
                    $"Failed to load module detail (HTTP {(int)statusCode}).",
                _ => "The API could not be reached. Please try again later."
            };
            ModelState.AddModelError(string.Empty, message);
        }

        return Page();
    }
}