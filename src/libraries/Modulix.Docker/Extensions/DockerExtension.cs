using System.Net;
using System.Net.Sockets;

using System.Globalization;

using Docker.DotNet;
using Docker.DotNet.Models;

using Microsoft.Extensions.Logging;

namespace Modulix.Docker.Extensions;

/// <summary>
/// Provides extension methods for working with Docker, including image management and port probing.
/// </summary>
public class DockerExtension
{
    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<DockerExtension> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerExtension"/> class with the specified logger.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public DockerExtension(ILogger<DockerExtension> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Tries to delete the specified Docker image for the given module.
    /// </summary>
    /// <param name="imageName">The name of the Docker image to delete.</param>
    /// <param name="moduleId">The ID of the module associated with the image.</param>
    /// <param name="_client">The Docker client.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task TryDeleteImageAsync(string? imageName, Guid moduleId, DockerClient _client, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(imageName))
            return;

        try
        {
            await _client.Images.DeleteImageAsync(imageName, new ImageDeleteParameters { Force = true }, ct);
            _logger.LogInformation("Docker image '{ImageName}' deleted for module '{ModuleId}'.", imageName, moduleId);
        }
        catch (DockerImageNotFoundException ex)
        {
            _logger.LogWarning(ex, "Docker image '{ImageName}' was not found for module '{ModuleId}'.", imageName, moduleId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to delete Docker image '{ImageName}' for module '{ModuleId}'.", imageName, moduleId);
        }
    }

    /// <summary>
    /// Ensures that the host data directory exists for the specified storage path.
    /// </summary>
    /// <param name="storagePath">The storage path.</param>
    /// <returns>The full path to the host data directory.</returns>
    public static string EnsureHostDataDirectory(string storagePath)
    {
        var fullStoragePath = Path.GetFullPath(storagePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentDir = Directory.GetParent(fullStoragePath);

        var hostDataPath = parentDir is not null &&
            string.Equals(parentDir.Name, "versions", StringComparison.OrdinalIgnoreCase) &&
            parentDir.Parent is { } moduleRootDir
            ? Path.Combine(moduleRootDir.FullName, "data")
            : Path.Combine(fullStoragePath, "data");

        Directory.CreateDirectory(hostDataPath);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                hostDataPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        }

        return hostDataPath;
    }

    /// <summary>
    /// Tries to get the bound host port for the specified container.
    /// </summary>
    /// <param name="container">The container to inspect.</param>
    /// <returns>The bound host port if found; otherwise, null.</returns>
    public static int? TryGetBoundHostPort(ContainerInspectResponse container)
    {
        var portBindings = container.HostConfig?.PortBindings ?? container.NetworkSettings?.Ports;
        if (portBindings is null)
            return null;

        foreach (var bindings in portBindings.Values)
        {
            if (bindings is null)
                continue;

            foreach (var binding in bindings)
            {
                if (int.TryParse(binding?.HostPort, NumberStyles.None, CultureInfo.InvariantCulture, out var port) &&
                    port is > 0 and <= 65535)
                {
                    return port;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Probes the specified TCP port to check if it is open.
    /// </summary>
    /// <param name="port">The TCP port to probe.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True if the port is open; otherwise, false.</returns>
    public static async Task<bool> ProbeTcpPortAsync(int port, CancellationToken ct)
    {
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(1));

        using var tcpClient = new TcpClient();
        try
        {
            await tcpClient.ConnectAsync(IPAddress.Loopback, port, connectCts.Token);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

}