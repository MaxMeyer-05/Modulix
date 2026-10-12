using System.Net;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Modulix.Client;
using Modulix.Client.Models.Dtos;

namespace Modulix.Web.Pages;

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
            Modules = await _modulixApiClient.ModuleManagement.GetAllModulesAsync(
                ct: HttpContext.RequestAborted);
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
}
