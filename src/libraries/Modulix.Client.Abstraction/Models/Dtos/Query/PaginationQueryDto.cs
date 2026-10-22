namespace Modulix.Client.Models.Dtos.Query;

/// <summary>
/// Represents a query for pagination, including page number and page size.
/// </summary>
public class PaginationQueryDto
{
    /// <summary>
    /// The default 1-based page number.
    /// </summary>
    public const int DefaultPage = 1;

    /// <summary>
    /// The default number of items per page.
    /// </summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// The maximum allowed number of items per page.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// The current page number (1-based).
    /// </summary>
    public int Page { get; set; } = DefaultPage;

    /// <summary>
    /// The number of items per page.
    /// </summary>
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>
    /// The search query string.
    /// </summary>
    public string Search { get; set; } = string.Empty;

    /// <summary>
    /// The sort field for the query.
    /// </summary>
    public string SortBy { get; set; } = string.Empty;
}