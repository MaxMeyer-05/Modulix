namespace Modulix.Client.Models.Dtos;

/// <summary>
/// Represents a query for pagination, including page number and page size.
/// </summary>
public class PaginationQueryDto
{
    /// <summary>
    /// The current page number.
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// The number of items per page.
    /// </summary>
    public int PageSize { get; set; }
}