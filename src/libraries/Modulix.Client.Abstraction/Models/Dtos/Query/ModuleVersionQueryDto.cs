namespace Modulix.Client.Models.Dtos.Query;

/// <summary>
/// Represents the query parameters for filtering modules based on their version.
/// </summary>
public class ModuleVersionQueryDto
{
    /// <summary>
    /// The version of the module to filter by.
    /// </summary>
    public string? Version { get; set; }
}