using System.ComponentModel.DataAnnotations;

namespace Modulix.Settings;

/// <summary>
/// Defines the shared storage root for module files and Dockerfile templates.
/// </summary>
public sealed class StorageSettings
{
    /// <summary>
    /// The configuration section containing the storage settings.
    /// </summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// The storage root, absolute or relative to the application's content root.
    /// </summary>
    [Required]
    public string StorageBasePath { get; set; } = "storage";
}
