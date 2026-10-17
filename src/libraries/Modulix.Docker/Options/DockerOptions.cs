using System.ComponentModel.DataAnnotations;

namespace Modulix.Docker.Options;

/// <summary>
/// Defines configuration settings for Docker containers, images, networks, and endpoint scanning.
/// </summary>
public sealed class DockerOptions
{
    /// <summary>
    /// The configuration section containing the Docker settings.
    /// </summary>
    public const string SectionName = "Docker";

    /// <summary>
    /// The name of the Docker network used for module containers.
    /// </summary>
    [Required]
    public string NetworkName { get; set; } = "modulix-network";

    /// <summary>
    /// The base Docker image for module containers.
    /// </summary>
    [Required]
    public string BaseImage { get; set; } = "mcr.microsoft.com/dotnet/aspnet:10.0";

    /// <summary>
    /// The Docker image used for scanning modules.
    /// </summary>
    [Required]
    public string ScannerImage { get; set; } = "modulix-scanner:latest";

    /// <summary>
    /// The maximum number of concurrent scans allowed.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxConcurrentScans { get; set; } = 4;
}