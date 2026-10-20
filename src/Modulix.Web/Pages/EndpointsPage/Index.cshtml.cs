using System.Globalization;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Modulix.Client;
using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;
using Modulix.Web.Services;

namespace Modulix.Web.Pages.EndpointsPage;

/// <summary>
/// Handles the display and filtering of all registered module sub-endpoints.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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
    /// The filtered and sorted list of module sub-endpoints.
    /// </summary>
    public List<ModuleEndpointsDetailDto> Endpoints { get; private set; } = [];

    /// <summary>
    /// Available distinct module names for filtering.
    /// </summary>
    public List<string> AvailableModules { get; private set; } = [];

    /// <summary>
    /// Total number of endpoints across all modules before filtering.
    /// </summary>
    public int TotalEndpoints { get; private set; }

    /// <summary>
    /// Number of active endpoints.
    /// </summary>
    public int ActiveCount { get; private set; }

    /// <summary>
    /// Number of endpoints pending confirmation.
    /// </summary>
    public int PendingCount { get; private set; }

    /// <summary>
    /// Number of inactive endpoints.
    /// </summary>
    public int InactiveCount { get; private set; }

    /// <summary>
    /// The current search query.
    /// </summary>
    public string Query { get; private set; } = "";

    /// <summary>
    /// The current HTTP method filter.
    /// </summary>
    public string MethodFilter { get; private set; } = "";

    /// <summary>
    /// The current endpoint status filter.
    /// </summary>
    public string StatusFilter { get; private set; } = "";

    /// <summary>
    /// The current module name filter.
    /// </summary>
    public string ModuleFilter { get; private set; } = "";

    /// <summary>
    /// The current sort order.
    /// </summary>
    public string SortBy { get; private set; } = "newest";

    /// <summary>
    /// Handles the GET request for the API endpoints page.
    /// </summary>
    /// <param name="q">The search query.</param>
    /// <param name="method">The HTTP method filter.</param>
    /// <param name="status">The endpoint status filter.</param>
    /// <param name="module">The parent module name filter.</param>
    /// <param name="sort">The sort order.</param>
    public async Task<IActionResult> OnGetAsync(string? q, string? method, string? status, string? module, string? sort)
    {
        Query = q?.Trim() ?? "";
        MethodFilter = method?.Trim().ToUpperInvariant() ?? "";
        StatusFilter = status?.Trim() ?? "";
        ModuleFilter = module?.Trim() ?? "";
        SortBy = sort is "path" or "module" ? sort : "newest";

        try
        {
            using var response = await _modulixApiClient.SubEndpointManagement.GetAllEndpointsDetailAsync(
                ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
                ModelState.AddModelError(string.Empty, error.DisplayMessage);
                return Page();
            }

            var allEndpoints = await response.Content.ReadFromJsonAsync<List<ModuleEndpointsDetailDto>>(
                cancellationToken: HttpContext.RequestAborted) ?? [];

            _logger.LogDebug("Successfully loaded {EndpointCount} detailed endpoints.", allEndpoints.Count);

            TotalEndpoints = allEndpoints.Count;
            ActiveCount = allEndpoints.Count(e => e.Status == ModuleEndpointsStatus.Active);
            PendingCount = allEndpoints.Count(e => e.Status == ModuleEndpointsStatus.PendingConfirmation);
            InactiveCount = allEndpoints.Count(e => e.Status == ModuleEndpointsStatus.Inactive);

            AvailableModules = allEndpoints
                .Select(e => e.ModuleName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true))
                .ToList();

            var filtered = allEndpoints.Where(endpoint =>
                $"{endpoint.ModuleName} {endpoint.HttpMethod} {endpoint.EndpointPath} {endpoint.Id}"
                    .Contains(Query, StringComparison.OrdinalIgnoreCase)
                && (MethodFilter.Length == 0 || endpoint.HttpMethod.Equals(MethodFilter, StringComparison.OrdinalIgnoreCase))
                && (StatusFilter.Length == 0 || endpoint.Status.ToString().Equals(StatusFilter, StringComparison.OrdinalIgnoreCase))
                && (ModuleFilter.Length == 0 || endpoint.ModuleName.Equals(ModuleFilter, StringComparison.OrdinalIgnoreCase)));

            Endpoints = (SortBy switch
            {
                "path" => filtered
                    .OrderBy(e => e.EndpointPath, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true))
                    .ThenBy(e => e.HttpMethod, StringComparer.OrdinalIgnoreCase),
                "module" => filtered
                    .OrderBy(e => e.ModuleName, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true))
                    .ThenBy(e => e.EndpointPath, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true)),
                _ => filtered.OrderByDescending(e => e.CreatedAtUtc)
            }).ToList();
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "ERROR loading API endpoints.");
            ModelState.AddModelError(string.Empty, "The API could not be reached. Please try again later.");
        }

        return Page();
    }
}
