using Modulix.Client.Models.Enums;

namespace Modulix.Client.Models.Dtos.Query;

/// <summary>
/// Represents the query parameters for filtering sub-endpoints based on their status.
/// </summary>
public class SubEndpointQueryDto
{
    /// <summary>
    /// The HTTP method of the sub-endpoint to filter by.
    /// </summary>
    public HttpMethod? HttpMethod { get; set; }

    /// <summary>
    /// The status of the sub-endpoint to filter by.
    /// </summary>
    public ModuleEndpointsStatus? Status { get; set; }
}