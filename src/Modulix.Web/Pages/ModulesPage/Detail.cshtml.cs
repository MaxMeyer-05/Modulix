using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

using Modulix.Client;
using Modulix.Client.Models.Dtos;
using Modulix.Web.Services;

namespace Modulix.Web.Pages.ModulesPage;

/// <summary>
/// Handles the display and management of module details.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public partial class DetailModel : PageModel
{
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
    private readonly ILogger<DetailModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DetailModel"/> class.
    /// </summary>
    /// <param name="modulixApiClient">The Modulix API client.</param>
    /// <param name="userActivityService">The user session activity service.</param>
    /// <param name="logger">The logger instance.</param>
    public DetailModel(
        IModulixApi modulixApiClient,
        IUserActivityService userActivityService,
        ILogger<DetailModel> logger)
    {
        _modulixApiClient = modulixApiClient;
        _userActivityService = userActivityService;
        _logger = logger;
    }

    /// <summary>
    /// Builds a URL for the module detail page preserving pagination and log tail parameters.
    /// </summary>
    public string GetPageUrl(Guid id, int targetPage)
    {
        var baseUrl = Url.Page("/ModulesPage/Detail", new { id }) ?? $"/modules/{id}";
        var queryParams = new Dictionary<string, string?>();

        if (targetPage > 1)
            queryParams["page"] = targetPage.ToString(CultureInfo.InvariantCulture);
        if (PageSize != DefaultEndpointsPageSize)
            queryParams["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture);
        if (LogsTail != DefaultContainerLogsTail)
            queryParams["logsTail"] = LogsTail.ToString(CultureInfo.InvariantCulture);

        return QueryHelpers.AddQueryString(baseUrl, queryParams);
    }

    /// <summary>
    /// Loads the module detail for the specified module ID.
    /// </summary>
    /// <param name="id">The ID of the module to load.</param>
    /// <param name="pageNumber">The 1-based page number for the module's endpoints.</param>
    /// <param name="pageSize">The number of endpoints per page.</param>
    /// <param name="logsTail">The number of container log lines from the end to load.</param>
    public async Task<IActionResult> OnGetAsync(
        Guid id,
        [FromQuery(Name = "page")] int pageNumber = PaginationQueryDto.DefaultPage,
        [FromQuery(Name = "pageSize")] int pageSize = DefaultEndpointsPageSize,
        [FromQuery(Name = "logsTail")] int logsTail = DefaultContainerLogsTail)
    {
        if (TempData[FeedbackKey] is string json)
            Feedback = JsonSerializer.Deserialize<ActionFeedback>(json);

        await LoadModuleAsync(id, pageNumber, pageSize, logsTail);
        return Page();
    }

    private async Task LoadModuleAsync(
        Guid id,
        int pageNumber = PaginationQueryDto.DefaultPage,
        int pageSize = DefaultEndpointsPageSize,
        int logsTail = DefaultContainerLogsTail)
    {
        CurrentPage = pageNumber < 1 ? PaginationQueryDto.DefaultPage : pageNumber;
        PageSize = pageSize < 1
            ? DefaultEndpointsPageSize
            : Math.Min(pageSize, PaginationQueryDto.MaxPageSize);
        LogsTail = AllowedLogsTailSizes.Contains(logsTail) ? logsTail : DefaultContainerLogsTail;

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

            await LoadContainerLogsAsync(id);

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

    private static ActionError Failure(int statusCode, string description) =>
        new(statusCode, ReasonPhrases.GetReasonPhrase(statusCode), description);
}