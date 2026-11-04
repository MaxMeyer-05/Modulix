using System.ComponentModel.DataAnnotations;

namespace Modulix.Settings;

/// <summary>
/// Defines module archive limits and version retention settings.
/// </summary>
public sealed class ModuleSettings
{
    /// <summary>
    /// The configuration section containing the shared settings.
    /// </summary>
    public const string SectionName = "Module";

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

    /// <summary>
    /// The maximum number of retained versions per module.
    /// </summary>
    /// <remarks>
    /// 0 means unlimited retention of previous versions.
    /// </remarks>
    [Range(0, int.MaxValue)]
    public int MaxRetainedVersionsPerModule { get; set; } = 5;
}