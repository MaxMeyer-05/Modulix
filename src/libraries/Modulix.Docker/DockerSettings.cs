using System.ComponentModel.DataAnnotations;

namespace Modulix.Docker;

/// <summary>
/// Defines configuration settings for Docker containers, images, networks, and endpoint scanning.
/// </summary>
public sealed class DockerSettings
{
    /// <summary>
    /// The configuration section containing the Docker settings.
    /// </summary>
    public const string SectionName = "Docker";

    /// <summary>
    /// The optional URI of the Docker or Podman daemon socket (e.g., <c>unix:///run/user/1000/podman/podman.sock</c> or <c>npipe://./pipe/docker_engine</c>).
    /// When not specified, the platform default socket is used.
    /// </summary>
    public string? SocketUri { get; set; }

    /// <summary>
    /// The maximum number of concurrent scans allowed.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxConcurrentScans { get; set; } = 4;
}