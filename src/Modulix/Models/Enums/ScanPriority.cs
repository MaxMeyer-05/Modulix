namespace Modulix.Models.Enums;

/// <summary>
/// Represents the priority of a scan operation.
/// </summary>
public enum ScanPriority
{
    /// <summary>
    /// Highest priority (lower number = earlier in the queue).
    /// </summary>
    Patch = 1,

    /// <summary>
    /// Normal priority.
    /// </summary>
    Create = 2 
}