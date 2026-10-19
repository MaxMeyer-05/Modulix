using System.Text.Json;
using System.Formats.Tar;
using System.Globalization;

using Docker.DotNet;
using Docker.DotNet.Models;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

using Modulix.Docker.Options;

namespace Modulix.Docker.Services;

/// <inheritdoc cref="IDockerService"/>
public class DockerService : IDockerService, IDisposable
{
    /// <summary>
    /// The shared module settings.
    /// </summary>
    private readonly DockerOptions _options;

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
    /// <param name="options">The shared module settings.</param>
    /// <param name="client">The Docker client owned by this service.</param>
    public DockerService(
        ILogger<DockerService> logger, 
        IOptions<DockerOptions> options,
        DockerClient client)
    {
        _logger = logger;
        _options = options.Value;
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<string> BuildContainerAsync(Guid moduleId, string storagePath, string entryDllName, int containerPort, CancellationToken ct = default)
    {
        var networks = await _client.Networks.ListNetworksAsync(new NetworksListParameters(), ct);
        if (!networks.Any(n => n.Name == _options.NetworkName))
        {
            await _client.Networks.CreateNetworkAsync(new NetworksCreateParameters
            {
                Name = _options.NetworkName,
                Driver = "bridge"
            }, ct);
            _logger.LogInformation("Docker network '{NetworkName}' created.", _options.NetworkName);
        }

        var version = Guid.NewGuid().ToString("N");
        var imageName = $"modulix-module-{moduleId:N}:{version}";
        var containerName = $"modulix-container-{moduleId:N}-{version}";
        var dockerfilePath = Path.Combine(storagePath, "Dockerfile");

        if (!File.Exists(dockerfilePath))
        {
            var defaultDockerfile = $"""
                FROM {_options.BaseImage}
                WORKDIR /app
                COPY . .
                ENV ASPNETCORE_ENVIRONMENT=Production
                ENTRYPOINT {JsonSerializer.Serialize(new[] { "dotnet", entryDllName })}
            """;

            await File.WriteAllTextAsync(dockerfilePath, defaultDockerfile, ct);
        }

        await using var tarStream = new MemoryStream();
        TarFile.CreateFromDirectory(storagePath, tarStream, includeBaseDirectory: false);
        tarStream.Position = 0;
        _logger.LogInformation("Docker build context created for module '{ModuleId}'.", moduleId);

        var progress = new DockerBuildProgress(_logger, moduleId);
        var portString = containerPort.ToString(CultureInfo.InvariantCulture);

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
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    [$"{portString}/tcp"] = default
                },
                Env =
                [
                    $"ASPNETCORE_HTTP_PORTS={portString}",
                    "ASPNETCORE_ENVIRONMENT=Production"
                ],
                HostConfig = new HostConfig
                {
                    NetworkMode = _options.NetworkName,
                    PublishAllPorts = false,
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        [$"{portString}/tcp"] = [new PortBinding { HostIP = "127.0.0.1", HostPort = portString }]
                    },
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
    public async Task<bool> TryRemoveModuleContainerAsync(string containerId, Guid moduleId)
    {
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await RemoveContainerAsync(containerId, moduleId, cleanupCts.Token);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up container '{ContainerId}' for module '{ModuleId}'.", containerId, moduleId);
            return false;
        }
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
                if (health is null or "healthy")
                    return;

                if (health == "unhealthy")
                    throw new InvalidOperationException($"Container '{containerId}' failed its health check.");

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

    /// <summary>
    /// Represents the progress of a Docker build operation.
    /// </summary>
    private sealed class DockerBuildProgress(ILogger<DockerService> logger, Guid moduleId) : IProgress<JSONMessage>
    {
        /// <summary>
        /// Gets the error message from the Docker build operation, if any.
        /// </summary>
        public string? ErrorMessage { get; private set; }

        /// <summary>
        /// Gets the error message, if any, from the Docker build operation.
        /// </summary>
        /// <param name="message">The JSON message from the Docker build operation.</param>
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