using Modulix.Client.Models.Enums;

namespace Modulix.Client.Models.Dtos.Query;

/// <summary>
/// Represents the query parameters for filtering modules based on their status.
/// </summary>
public class ModuleQueryDto
{
    /// <summary>
    /// The status of the module to filter by.
    /// </summary>
    public ModuleStatus? Status { get; set; }
}