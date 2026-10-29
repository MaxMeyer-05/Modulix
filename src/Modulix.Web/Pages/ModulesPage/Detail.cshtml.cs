using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

using Modulix.Client;
using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;
using Modulix.Client.Models.Enums;
using Modulix.Web.Services;

namespace Modulix.Web.Pages.ModulesPage;

/// <summary>
/// Handles the display and management of module details.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public partial class DetailModel : PageModel
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The Modulix API client.
    /// </summary>
    private readonly IModulixApi _modulixApiClient;

    /// <summary>
    /// The user session activity service.
    /// </summary>
    private readonly IUserActivityService _userActivityService;

    /// <summary>
    /// The endpoint discrepancy report service.
    /// </summary>
    private readonly IEndpointDiscrepancyService? _endpointDiscrepancyService;

    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<DetailModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DetailModel"/> class.
    /// </summary>
    /// <param name="modulixApiClient">The Modulix API client.</param>
    /// <param name="userActivityService">The user session activity service.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="endpointDiscrepancyService">The endpoint discrepancy report service.</param>
    public DetailModel(
        IModulixApi modulixApiClient,
        IUserActivityService userActivityService,
        ILogger<DetailModel> logger,
        IEndpointDiscrepancyService? endpointDiscrepancyService = null)
    {
        _modulixApiClient = modulixApiClient;
        _userActivityService = userActivityService;
        _logger = logger;
        _endpointDiscrepancyService = endpointDiscrepancyService;
    }

    /// <summary>
    /// Builds a URL for the module detail page preserving pagination parameters.
    /// </summary>
    public string GetPageUrl(Guid id, int targetPage)
    {
        var baseUrl = Url.Page("/ModulesPage/Detail", new { id }) ?? $"/modules/{id}";
        var queryParams = new Dictionary<string, string?>();

        if (targetPage > 1)
            queryParams["page"] = targetPage.ToString(CultureInfo.InvariantCulture);
        if (PageSize != DefaultEndpointsPageSize)
            queryParams["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture);

        return QueryHelpers.AddQueryString(baseUrl, queryParams);
    }

    /// <summary>
    /// Loads the module detail for the specified module ID.
    /// </summary>
    /// <param name="id">The ID of the module to load.</param>
    /// <param name="pageNumber">The 1-based page number for the module's endpoints.</param>
    /// <param name="pageSize">The number of endpoints per page.</param>
    public async Task<IActionResult> OnGetAsync(
        Guid id,
        [FromQuery(Name = "page")] int pageNumber = PaginationQueryDto.DefaultPage,
        [FromQuery(Name = "pageSize")] int pageSize = DefaultEndpointsPageSize)
    {
        if (TempData[FeedbackKey] is string json)
            Feedback = JsonSerializer.Deserialize<ActionFeedback>(json);

        if (TempData[DiscrepancyReportKey] is string discrepancyJson)
        {
            DiscrepancyReport = JsonSerializer.Deserialize<SubEndpointDiscrepancyReportDto>(discrepancyJson, WebJsonOptions);
            if (DiscrepancyReport is not null)
                _endpointDiscrepancyService?.SaveReport(id, DiscrepancyReport);
        }

        await LoadModuleAsync(id, pageNumber, pageSize);
        return Page();
    }

    private async Task LoadModuleAsync(
        Guid id,
        int pageNumber = PaginationQueryDto.DefaultPage,
        int pageSize = DefaultEndpointsPageSize)
    {
        CurrentPage = pageNumber < 1 ? PaginationQueryDto.DefaultPage : pageNumber;
        PageSize = pageSize < 1
            ? DefaultEndpointsPageSize
            : Math.Min(pageSize, PaginationQueryDto.MaxPageSize);

        DiscrepancyReport ??= _endpointDiscrepancyService?.GetReport(id);

        try
        {
            _logger.LogDebug("Loading module detail for ID {ModuleId}", id);

            using var response = await _modulixApiClient.ModuleManagement.GetModuleByIdAsync(
                moduleId: id,
                ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
                ModelState.AddModelError(string.Empty, error.DisplayMessage);
                return;
            }

            Module = await response.Content.ReadFromJsonAsync<ModuleDetailDto>(
                cancellationToken: HttpContext.RequestAborted);

            if (Module is null)
                return;

            ResolveDiscrepancyReport(id);

            if (Module.Status == ModuleStatus.Running || !string.IsNullOrWhiteSpace(Module.ContainerId))
            {
                using var statsResponse = await _modulixApiClient.ModuleManagement.GetModuleStatsAsync(
                    moduleId: id,
                    ct: HttpContext.RequestAborted);

                if (statsResponse.IsSuccessStatusCode)
                {
                    ModuleStats = await statsResponse.Content.ReadFromJsonAsync<ModuleStatsDto>(
                        cancellationToken: HttpContext.RequestAborted);
                }
            }

            using var endpointsResponse = await _modulixApiClient.SubEndpointManagement.GetModuleEndpointsAsync(
                moduleId: id,
                pagination: new PaginationQueryDto { Page = CurrentPage, PageSize = PageSize },
                ct: HttpContext.RequestAborted);

            if (endpointsResponse.IsSuccessStatusCode)
            {
                var pagedEndpoints = await endpointsResponse.Content.ReadFromJsonAsync<PagedResultDto<ModuleSubEndpointDto>>(
                    cancellationToken: HttpContext.RequestAborted);

                if (pagedEndpoints is not null)
                {
                    Endpoints = pagedEndpoints.Items;
                    TotalEndpoints = pagedEndpoints.TotalCount;
                    CurrentPage = pagedEndpoints.Page > 0 ? pagedEndpoints.Page : CurrentPage;
                    PageSize = pagedEndpoints.PageSize > 0 ? pagedEndpoints.PageSize : PageSize;
                    TotalPages = Math.Max(1, pagedEndpoints.TotalPages);
                    HasPreviousPage = pagedEndpoints.HasPreviousPage;
                    HasNextPage = pagedEndpoints.HasNextPage;
                    return;
                }
            }

            var fallbackEndpoints = Module.Endpoints.ToList();
            TotalEndpoints = fallbackEndpoints.Count;
            TotalPages = Math.Max(1, (int)Math.Ceiling((double)TotalEndpoints / PageSize));
            HasPreviousPage = CurrentPage > 1;
            HasNextPage = CurrentPage < TotalPages;
            Endpoints = fallbackEndpoints
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "ERROR loading module detail.");
            ModelState.AddModelError(string.Empty, "The API could not be reached. Please try again later.");
        }
    }

    private void ResolveDiscrepancyReport(Guid id)
    {
        if (Module is null)
            return;

        if (!HasPendingEndpoints)
        {
            DiscrepancyReport = null;
            _endpointDiscrepancyService?.ClearReport(id);
            return;
        }

        if (DiscrepancyReport is not null)
            return;

        if (OrderedVersions.Count > 1)
        {
            var matched = Module.Endpoints
                .Where(e => e.Status == ModuleEndpointsStatus.Active)
                .Select(e => new DiscoveredSubEndpointDto { HttpMethod = e.HttpMethod, EndpointPath = e.EndpointPath })
                .ToList();

            var extra = PendingEndpoints
                .Where(e => string.Equals(e.DiscoveredInVersion, Module.CurrentVersion, StringComparison.OrdinalIgnoreCase))
                .Select(e => new DiscoveredSubEndpointDto { HttpMethod = e.HttpMethod, EndpointPath = e.EndpointPath })
                .ToList();

            var missing = PendingEndpoints
                .Where(e => !string.Equals(e.DiscoveredInVersion, Module.CurrentVersion, StringComparison.OrdinalIgnoreCase))
                .Select(e => new DiscoveredSubEndpointDto { HttpMethod = e.HttpMethod, EndpointPath = e.EndpointPath })
                .ToList();

            if (extra.Count > 0 || missing.Count > 0)
            {
                DiscrepancyReport = new SubEndpointDiscrepancyReportDto
                {
                    ModuleId = id,
                    HasDiscrepancy = true,
                    MatchedEndpoints = matched,
                    ExtraEndpoints = extra,
                    MissingEndpoints = missing
                };
            }
        }
    }

    private static ActionError Failure(int statusCode, string description) =>
        new(statusCode, ReasonPhrases.GetReasonPhrase(statusCode), description);
}
