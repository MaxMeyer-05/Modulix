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
    public async Task<string> BuildContainerAsync(Guid moduleId, string storagePath, string entryDllName, int containerPort, CancellationToken ct = default)
    {
        // Ensure the Docker network exists before building the container.
        var networks = await _client.Networks.ListNetworksAsync(new NetworksListParameters(), ct);
        if (!networks.Any(n => n.Name == NetworkName)) 
        {
            await _client.Networks.CreateNetworkAsync(new NetworksCreateParameters
            {
                Name = NetworkName,
                Driver = "bridge"
            }, ct);
            _logger.LogInformation("Docker network '{NetworkName}' created.", NetworkName);
        }

        // Define the image and container names based on the module ID.
        var imageName = $"modulix-module-{moduleId}";
        var containerName = $"modulix-container-{moduleId}";

        var dockerfilePath = Path.Combine(storagePath, "Dockerfile");

        var defaultDockerfile = $"""
            FROM {BaseImage}
            WORKDIR /app
            COPY . .
            ENV ASPNETCORE_ENVIRONMENT=Production
            ENTRYPOINT ["dotnet", "{entryDllName}"]
        """;

        await File.WriteAllTextAsync(dockerfilePath, defaultDockerfile, ct);
        
        // Create a tarball of the build context for Docker.
        var tarStream = new MemoryStream();
        TarFile.CreateFromDirectory(storagePath, tarStream, includeBaseDirectory: false);
        tarStream.Position = 0;

        _logger.LogInformation("Docker build context created for module '{ModuleId}'.", moduleId);

        // Build the Docker image from the Dockerfile and the tarball context.
        await _client.Images.BuildImageFromDockerfileAsync(
            new ImageBuildParameters
            {
                Tags = [imageName],
                Dockerfile = "Dockerfile"
            },
            tarStream,
            [],
            new Dictionary<string, string>(),
            new Progress<JSONMessage>(msg =>
            {
                if (!string.IsNullOrWhiteSpace(msg.Stream))
                {
                    _logger.LogDebug("[Docker Build: {ModuleId}] {Message}", moduleId, msg.Stream.TrimEnd());
                }
                if (!string.IsNullOrWhiteSpace(msg.ErrorMessage))
                {
                    _logger.LogError("[Docker Build Error: {ModuleId}] {Message}", moduleId, msg.ErrorMessage);
                }
            }),
            ct
        );

        // Create the Docker container using the built image.
        var createResponse = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = imageName,
            Name = containerName,
            Env =
            [
                $"ASPNETCORE_HTTP_PORTS={containerPort}",
                "ASPNETCORE_ENVIRONMENT=Production"
            ],
            HostConfig = new HostConfig
            {
                NetworkMode = NetworkName,
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped }
            }
        }, ct); 

        _logger.LogInformation("Container '{ContainerName}' created for module '{ModuleId}'.", containerName, moduleId);

        return createResponse.ID;
    }

    /// <inheritdoc/>
    public async Task RemoveContainerAsync(string containerId, Guid moduleId, CancellationToken ct = default)
    {
        var imageName = $"modulix-module-{moduleId}";
        await StopContainerAsync(containerId, ct);

        try
        {
            await _client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }, ct);
            _logger.LogInformation("Container '{ContainerId}' removed.", containerId);
        }
        catch (DockerContainerNotFoundException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove container '{ContainerId}'.", containerId);
        }

        try
        {
            await _client.Images.DeleteImageAsync(imageName, new ImageDeleteParameters { Force = true }, ct);
            _logger.LogInformation("Docker image '{ImageName}' deleted.", imageName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete image '{ImageName}'. Skipping.", imageName);
        }
    }

    /// <inheritdoc/>
    public async Task RunContainerAsync(string containerId, CancellationToken ct = default)
    {
        await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct);
        _logger.LogInformation("Container '{ContainerId}' started.", containerId);
    }

    /// <inheritdoc/>
    public async Task StopContainerAsync(string containerId, CancellationToken ct = default)
    {
        try
        {
            await _client.Containers.StopContainerAsync(containerId, new ContainerStopParameters { WaitBeforeKillSeconds = 5 }, ct);
            _logger.LogInformation("Container '{ContainerId}' stopped.", containerId);
        }
        catch (DockerContainerNotFoundException)
        {
            _logger.LogWarning("Container '{ContainerId}' was not found or already stopped.", containerId);
        }
    }
}