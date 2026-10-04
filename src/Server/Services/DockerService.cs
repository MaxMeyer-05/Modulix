using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Text.Json;

using Docker.DotNet;
using Docker.DotNet.Models;

using Server.Services.Interfaces;

namespace Server.Services;

/// <inheritdoc cref="IDockerService"/>
public class DockerService : IDockerService, IDisposable
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

    /// <summary>
    /// Initializes the service with an existing Docker client.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="client">The Docker client owned by this service.</param>
    public DockerService(ILogger<DockerService> logger, DockerClient client)
    {
        _logger = logger;
        _client = client;
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
        var version = Guid.NewGuid().ToString("N");
        var imageName = $"modulix-module-{moduleId:N}:{version}";
        var containerName = $"modulix-container-{moduleId:N}-{version}";

        var dockerfilePath = Path.Combine(storagePath, "Dockerfile");

        var defaultDockerfile = $"""
            FROM {BaseImage}
            WORKDIR /app
            COPY . .
            ENV ASPNETCORE_ENVIRONMENT=Production
            ENTRYPOINT {JsonSerializer.Serialize(new[] { "dotnet", entryDllName })}
        """;

        await File.WriteAllTextAsync(dockerfilePath, defaultDockerfile, ct);
        
        // Create a tarball of the build context for Docker.
        await using var tarStream = new MemoryStream();
        TarFile.CreateFromDirectory(storagePath, tarStream, includeBaseDirectory: false);
        tarStream.Position = 0;

        _logger.LogInformation("Docker build context created for module '{ModuleId}'.", moduleId);

        // Build the Docker image from the Dockerfile and the tarball context.
        var progress = new DockerBuildProgress(_logger, moduleId);
        try
        {
            await _client.Images.BuildImageFromDockerfileAsync(
                new ImageBuildParameters
                {
                    Tags = [imageName],
                    Dockerfile = "Dockerfile"
                },
                tarStream,
                [],
                new Dictionary<string, string>(),
                progress,
                ct
            );

            if (progress.ErrorMessage is not null)
                throw new InvalidOperationException($"Docker image build failed: {progress.ErrorMessage}");

            var createResponse = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = imageName,
                Name = containerName,
                Env =
                [
                    $"ASPNETCORE_HTTP_PORTS={containerPort}",
                    "ASPNETCORE_ENVIRONMENT=Production"
                ],
                Healthcheck = new HealthConfig
                {
                    Test = ["CMD", "bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/\"$ASPNETCORE_HTTP_PORTS\""],
                    Interval = TimeSpan.FromSeconds(1),
                    Timeout = TimeSpan.FromSeconds(2),
                    Retries = 30
                },
                HostConfig = new HostConfig
                {
                    NetworkMode = NetworkName,
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped }
                }
            }, ct);

            _logger.LogInformation("Container '{ContainerName}' created for module '{ModuleId}'.", containerName, moduleId);

            return createResponse.ID;
        }
        catch
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await _client.Containers.RemoveContainerAsync(containerName, new ContainerRemoveParameters { Force = true }, cleanupCts.Token);
            }
            catch (DockerContainerNotFoundException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up candidate container '{ContainerName}'.", containerName);
            }

            try
            {
                await _client.Images.DeleteImageAsync(imageName, new ImageDeleteParameters(), cleanupCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up candidate image '{ImageName}'.", imageName);
            }

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task RemoveContainerAsync(string containerId, Guid moduleId, CancellationToken ct = default)
    {
        ContainerInspectResponse container;
        try
        {
            container = await _client.Containers.InspectContainerAsync(containerId, ct);
        }
        catch (DockerContainerNotFoundException)
        {
            return;
        }

        await StopContainerAsync(containerId, ct);
        await _client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }, ct);
        _logger.LogInformation("Container '{ContainerId}' removed for module '{ModuleId}'.", containerId, moduleId);

        await _client.Images.DeleteImageAsync(container.Config.Image, new ImageDeleteParameters(), ct);
        _logger.LogInformation("Docker image '{ImageName}' deleted.", container.Config.Image);
    }

    /// <inheritdoc/>
    public async Task RunContainerAsync(string containerId, CancellationToken ct = default)
    {
        await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct);
        _logger.LogInformation("Container '{ContainerId}' started.", containerId);
    }

    /// <inheritdoc/>
    public async Task WaitUntilReadyAsync(string containerId, CancellationToken ct = default)
    {
        using var readinessCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readinessCts.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            while (true)
            {
                var container = await _client.Containers.InspectContainerAsync(containerId, readinessCts.Token);
                if (!container.State.Running || container.State.Restarting)
                    throw new InvalidOperationException($"Container '{containerId}' stopped before becoming ready.");

                var health = container.State.Health?.Status;
                if (health == "healthy")
                    return;

                if (health is null or "unhealthy")
                    throw new InvalidOperationException($"Container '{containerId}' has no successful port health check.");

                await Task.Delay(TimeSpan.FromSeconds(1), readinessCts.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Container '{containerId}' did not become ready within 60 seconds.");
        }
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

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class DockerBuildProgress(ILogger<DockerService> logger, Guid moduleId) : IProgress<JSONMessage>
    {
        public string? ErrorMessage { get; private set; }

        public void Report(JSONMessage message)
        {
            if (!string.IsNullOrWhiteSpace(message.Stream))
                logger.LogDebug("[Docker Build: {ModuleId}] {Message}", moduleId, message.Stream.TrimEnd());

            if (!string.IsNullOrWhiteSpace(message.ErrorMessage))
            {
                ErrorMessage ??= message.ErrorMessage;
                logger.LogError("[Docker Build Error: {ModuleId}] {Message}", moduleId, message.ErrorMessage);
            }
        }
    }
}