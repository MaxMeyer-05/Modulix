using System.Globalization;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

using Modulix.Client;
using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;
using Modulix.Client.Models.Dtos.Query;

using Modulix.Web.Services;

namespace Modulix.Web.Pages.EndpointsPage;

/// <summary>
/// Handles the display and filtering of all registered module sub-endpoints.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PageModel
{
    /// <summary>
    /// Allowed page size options for the endpoints list.
    /// </summary>
    public static readonly int[] AllowedPageSizes = [5, 10, 20, 50];

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
    public List<ModuleSubEndpointsDetailDto> Endpoints { get; private set; } = [];

    /// <summary>
    /// Available distinct module names for filtering.
    /// </summary>
    public List<string> AvailableModules { get; private set; } = [];

    /// <summary>
    /// Total number of endpoints across all modules before filtering.
    /// </summary>
    public int TotalEndpoints { get; private set; }

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
    /// The current 1-based page number.
    /// </summary>
    public int CurrentPage { get; private set; } = PaginationQueryDto.DefaultPage;

    /// <summary>
    /// The number of endpoints displayed per page.
    /// </summary>
    public int PageSize { get; private set; } = PaginationQueryDto.DefaultPageSize;

    /// <summary>
    /// The total number of pages available.
    /// </summary>
    public int TotalPages { get; private set; } = 1;

    /// <summary>
    /// Indicates whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage { get; private set; }

    /// <summary>
    /// Indicates whether there is a next page.
    /// </summary>
    public bool HasNextPage { get; private set; }

    /// <summary>
    /// Builds a URL for the endpoints page preserving current filter, sort, and pagination parameters.
    /// </summary>
    public string GetPageUrl(int targetPage)
    {
        var baseUrl = Url.Page("/EndpointsPage/Index") ?? "/endpoints";
        var queryParams = new Dictionary<string, string?>();

        if (!string.IsNullOrWhiteSpace(Query))
            queryParams["q"] = Query;
        if (!string.IsNullOrWhiteSpace(MethodFilter))
            queryParams["method"] = MethodFilter;
        if (!string.IsNullOrWhiteSpace(StatusFilter))
            queryParams["status"] = StatusFilter;
        if (!string.IsNullOrWhiteSpace(ModuleFilter))
            queryParams["module"] = ModuleFilter;
        if (!string.IsNullOrWhiteSpace(SortBy) && SortBy != "newest")
            queryParams["sort"] = SortBy;
        if (targetPage > 1)
            queryParams["page"] = targetPage.ToString(CultureInfo.InvariantCulture);
        if (PageSize != PaginationQueryDto.DefaultPageSize)
            queryParams["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture);

        return QueryHelpers.AddQueryString(baseUrl, queryParams);
    }

    /// <summary>
    /// Handles the GET request for the API endpoints page.
    /// </summary>
    /// <param name="q">The search query.</param>
    /// <param name="method">The HTTP method filter.</param>
    /// <param name="status">The endpoint status filter.</param>
    /// <param name="module">The parent module name filter.</param>
    /// <param name="sort">The sort order.</param>
    /// <param name="pageNumber">The 1-based page number.</param>
    /// <param name="pageSize">The number of items per page.</param>
    public async Task<IActionResult> OnGetAsync(
        string? q,
        string? method,
        string? status,
        string? module,
        string? sort,
        [FromQuery(Name = "page")] int pageNumber = PaginationQueryDto.DefaultPage,
        [FromQuery(Name = "pageSize")] int pageSize = PaginationQueryDto.DefaultPageSize)
    {
        Query = q?.Trim() ?? "";
        MethodFilter = method?.Trim().ToUpperInvariant() ?? "";
        StatusFilter = status?.Trim() ?? "";
        ModuleFilter = module?.Trim() ?? "";
        SortBy = sort is "path" or "module" ? sort : "newest";
        CurrentPage = pageNumber < 1 ? PaginationQueryDto.DefaultPage : pageNumber;
        PageSize = pageSize < 1
            ? PaginationQueryDto.DefaultPageSize
            : Math.Min(pageSize, PaginationQueryDto.MaxPageSize);

        try
        {
            var endpointQuery = new SubEndpointQueryDto
            {
                HttpMethod = !string.IsNullOrWhiteSpace(MethodFilter) ? MethodFilter : null,
                Status = Enum.TryParse<ModuleEndpointsStatus>(StatusFilter, ignoreCase: true, out var parsedStatus)
                    ? parsedStatus
                    : null
            };

            using var response = await _modulixApiClient.SubEndpointManagement.GetAllEndpointsDetailAsync(
                query: endpointQuery,
                pagination: new PaginationQueryDto { Page = CurrentPage, PageSize = PageSize },
                ct: HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.ReadApiErrorAsync(HttpContext.RequestAborted);
                ModelState.AddModelError(string.Empty, error.DisplayMessage);
                return Page();
            }

            var pagedResult = await response.Content.ReadFromJsonAsync<PagedResultDto<ModuleSubEndpointsDetailDto>>(
                cancellationToken: HttpContext.RequestAborted) ?? new PagedResultDto<ModuleSubEndpointsDetailDto>
            {
                Page = CurrentPage,
                PageSize = PageSize
            };

            var allEndpoints = pagedResult.Items;

            _logger.LogDebug("Successfully loaded {EndpointCount} of {TotalCount} detailed endpoints (page {Page}).", allEndpoints.Count, pagedResult.TotalCount, pagedResult.Page);

            TotalEndpoints = pagedResult.TotalCount;
            CurrentPage = pagedResult.Page > 0 ? pagedResult.Page : CurrentPage;
            PageSize = pagedResult.PageSize > 0 ? pagedResult.PageSize : PageSize;
            TotalPages = Math.Max(1, pagedResult.TotalPages);
            HasPreviousPage = pagedResult.HasPreviousPage;
            HasNextPage = pagedResult.HasNextPage;

            AvailableModules = allEndpoints
                .Select(e => e.ModuleName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true))
                .ToList();

            var filtered = allEndpoints.Where(endpoint =>
                $"{endpoint.ModuleName} {endpoint.HttpMethod} {endpoint.EndpointPath} {endpoint.Id}"
                    .Contains(Query, StringComparison.OrdinalIgnoreCase)
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
