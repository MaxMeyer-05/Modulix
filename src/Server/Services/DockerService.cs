using System.Formats.Tar;
using System.Runtime.InteropServices;

using Docker.DotNet;
using Docker.DotNet.Models;

using Server.Services.Interfaces;

namespace Server.Services;

/// <inheritdoc cref="IDockerService"/>
public class DockerService : IDockerService
{
    /// <summary>
    /// The name of the Docker network used for module containers.
    /// </summary>
    private const string NetworkName = "modulix-network";

    /// <summary>
    /// The base Docker image for module containers.
    /// </summary>
    private const string BaseImage = "mcr.microsoft.com/dotnet/aspnet:10.0";

    /// <summary>
    /// The Docker client used to interact with the Docker daemon.
    /// </summary>
    private readonly DockerClient _client;

    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<DockerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public DockerService(ILogger<DockerService> logger)
    {
        _logger = logger;

        var dockerUri = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");

        _client = new DockerClientConfiguration(dockerUri).CreateClient();
    }

    /// <inheritdoc/>
    public Task<string> BuildContainerAsync(Guid moduleId, string storagePath, string entryDllName, int containerPort, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public Task RemoveContainerAsync(string containerId, Guid moduleId, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public Task RunContainerAsync(string containerId, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public Task StopContainerAsync(string containerId, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}