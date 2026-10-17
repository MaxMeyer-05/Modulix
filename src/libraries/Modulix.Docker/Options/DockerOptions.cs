using System.ComponentModel.DataAnnotations;

namespace Modulix.Docker.Options;

public class DockerOptions
{
    /// <summary>
    /// The name of the Docker network used for module containers.
    /// </summary>
    public required string NetworkName { get; set; } = "modulix-network";

    /// <summary>
    /// The base Docker image for module containers.
    /// </summary>
    public required string BaseImage { get; set; } = "mcr.microsoft.com/dotnet/aspnet:10.0";

    /// <summary>
    /// The Docker image used for scanning modules.
    /// </summary>
    public required string ScannerImage { get; set; } = "modulix-scanner:latest";

    /// <summary>
    /// The maximum number of concurrent scans allowed.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxConcurrentScans { get; set; } = 4;
}