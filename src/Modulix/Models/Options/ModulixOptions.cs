using System.ComponentModel.DataAnnotations;

namespace Modulix.Models.Options;

/// <summary>
/// Defines shared settings for module storage, Docker containers and endpoint scanning.
/// </summary>
public sealed class ModulixOptions
{
    /// <summary>
    /// The configuration section containing the shared settings.
    /// </summary>
    public const string SectionName = "Modulix";

    /// <summary>
    /// The module storage path, absolute or relative to the application's content root.
    /// </summary>
    [Required]
    public string StorageBasePath { get; set; } = Path.Combine("storage", "modules");

    /// <summary>
    /// The maximum number of entries allowed in a module archive.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaximumArchiveEntryCount { get; set; } = 1_000;

    /// <summary>
    /// The maximum allowed uncompressed size of a module archive in bytes.
    /// </summary>
    [Range(typeof(long), "1", "9223372036854775807")]
    public long MaximumArchiveUncompressedBytes { get; set; } = 512L * 1024 * 1024;
}