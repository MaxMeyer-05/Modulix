using System.Formats.Tar;
using System.Globalization;

using Docker.DotNet;
using Docker.DotNet.Models;

using Microsoft.Extensions.Logging;

using Modulix.Docker.Models;
using Modulix.Docker.Extensions;

using Modulix.Docker.Services.Interfaces;

namespace Modulix.Docker.Services;

/// <inheritdoc cref="IDockerService"/>
internal class DockerService : IDockerService, IDisposable
{
    /// <summary>
    /// The Docker client used to interact with the Docker daemon.
    /// </summary>
    private readonly DockerClient _client;

    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<DockerService> _logger;

    /// <summary>
    /// The Docker extension instance used for image management and port probing.
    /// </summary>
    private readonly DockerExtension _dockerExtension;

    /// <summary>
    /// Delegate used to probe whether a local TCP port is accepting connections.
    /// </summary>
    private readonly Func<int, CancellationToken, Task<bool>> _portProbe;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="client">The Docker client owned by this service.</param>
    /// <param name="dockerExtension">The Docker extension instance used for image management and port probing.</param>
    /// <param name="portProbe">Optional delegate for probing local TCP port readiness.</param>
    public DockerService(
        ILogger<DockerService> logger, 
        DockerClient client,
        DockerExtension dockerExtension,
        Func<int, CancellationToken, Task<bool>>? portProbe = null)
    {
        _logger = logger;
        _client = client;
        _dockerExtension = dockerExtension;
        _portProbe = portProbe ?? DockerExtension.ProbeTcpPortAsync;
    }

    /// <inheritdoc/>
    public async Task<string> BuildContainerAsync(
        string buildContextPath, 
        string dockerfileName, 
        string imageName, 
        string containerName, 
        string networkName,
        int containerPort, 
        IList<string>? env = null, 
        IList<string>? binds = null, 
        CancellationToken ct = default)
    {      
        var fullDockerfilePath = Path.Combine(buildContextPath, dockerfileName);
        if (!File.Exists(fullDockerfilePath))
        {
            throw new FileNotFoundException($"Dockerfile '{dockerfileName}' was not found in '{buildContextPath}'.", fullDockerfilePath);
        }

        await using var tarStream = new MemoryStream();
        TarFile.CreateFromDirectory(buildContextPath, tarStream, includeBaseDirectory: false);
        tarStream.Position = 0;

        _logger.LogInformation("Building Docker image '{ImageName}' from '{Path}' using '{Dockerfile}'...", 
            imageName, buildContextPath, dockerfileName);
        
        var progress = new DockerBuildProgress(_logger, containerName);
        var portString = containerPort.ToString(CultureInfo.InvariantCulture);

        try
        {
            await _client.Images.BuildImageFromDockerfileAsync(
                new ImageBuildParameters
                {
                    Tags = [imageName],
                    Dockerfile = dockerfileName
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
                Env = env ?? [],
                HostConfig = new HostConfig
                {
                    NetworkMode = networkName,
                    Binds = binds ?? [],
                    PublishAllPorts = false,
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        [$"{portString}/tcp"] = [new PortBinding { HostIP = "127.0.0.1", HostPort = portString }]
                    },
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped }
                }
            }, ct);

            _logger.LogInformation("Container '{ContainerName}' created successfully.", containerName);
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

            await _dockerExtension.TryDeleteImageAsync(imageName, containerName, _client, cleanupCts.Token);

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task EnsureNetworkExistsAsync(string networkName, CancellationToken ct = default)
    {
        var networks = await _client.Networks.ListNetworksAsync(new NetworksListParameters(), ct);
        if (!networks.Any(n => n.Name == networkName))
        {
            await _client.Networks.CreateNetworkAsync(new NetworksCreateParameters
            {
                Name = networkName,
                Driver = "bridge"
            }, ct);
            _logger.LogInformation("Docker network '{NetworkName}' created.", networkName);
        }
    }

    /// <inheritdoc/>
    public async Task RemoveContainerAsync(string containerId, string containerName, CancellationToken ct = default)
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

        _logger.LogDebug("Stopping and removing container '{ContainerId}' with name '{ContainerName}'.", containerId, containerName);

        try
        {
            await _client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }, ct);
            _logger.LogInformation("Container '{ContainerId}' removed with name '{ContainerName}'.", containerId, containerName);
        }
        catch (DockerContainerNotFoundException ex)
        {
            _logger.LogWarning(ex, "Container '{ContainerId}' was not found during removal with name '{ContainerName}'.", containerId, containerName);
        }
        catch (DockerApiException ex)
        {
            _logger.LogWarning(ex, "Failed to remove container '{ContainerId}' with name '{ContainerName}'.", containerId, containerName);
            throw;
        }

        await _dockerExtension.TryDeleteImageAsync(container.Config?.Image, containerName, _client, ct);
    }

    /// <inheritdoc/>
    public async Task<bool> TryRemoveModuleContainerAsync(string containerId, string containerName, CancellationToken ct = default)
    {
        using var cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cleanupCts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await RemoveContainerAsync(containerId, containerName, cleanupCts.Token);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up container '{ContainerId}' with name '{ContainerName}'.", containerId, containerName);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task RunContainerAsync(string containerId, CancellationToken ct = default)
    {
        try
        {
            await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct);
            _logger.LogInformation("Container '{ContainerId}' started.", containerId);
        }
        catch (DockerContainerNotFoundException ex)
        {
            _logger.LogWarning(ex, "Container '{ContainerId}' was not found during start.", containerId);
            throw;
        }
        catch (DockerApiException ex)
        {
            _logger.LogWarning(ex, "Failed to start container '{ContainerId}'.", containerId);
            throw;
        }
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
                if (!container.State.Running || container.State.Restarting || container.State.ExitCode != 0)
                    throw new InvalidOperationException($"Container '{containerId}' stopped before becoming ready (ExitCode: {container.State.ExitCode}).");

                var health = container.State.Health?.Status;
                if (string.Equals(health, "unhealthy", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Container '{containerId}' failed its health check.");

                if (string.Equals(health, "healthy", StringComparison.OrdinalIgnoreCase))
                    return;

                if (string.IsNullOrWhiteSpace(health) || string.Equals(health, "none", StringComparison.OrdinalIgnoreCase) || string.Equals(health, "unconfigured", StringComparison.OrdinalIgnoreCase))
                {
                    var hostPort = DockerExtension.TryGetBoundHostPort(container);
                    if (hostPort is null)
                        return;

                    if (await _portProbe(hostPort.Value, readinessCts.Token))
                    {
                        var verified = await _client.Containers.InspectContainerAsync(containerId, readinessCts.Token);
                        if (!verified.State.Running || verified.State.Restarting || verified.State.ExitCode != 0)
                            throw new InvalidOperationException($"Container '{containerId}' stopped before becoming ready (ExitCode: {verified.State.ExitCode}).");

                        return;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(1), readinessCts.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Container '{containerId}' did not become ready within 60 seconds.");
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsContainerRunningAsync(string containerId, CancellationToken ct = default)
    {
        try
        {
            var container = await _client.Containers.InspectContainerAsync(containerId, ct);
            if (!container.State.Running || container.State.Restarting || container.State.ExitCode != 0)
                return false;

            var health = container.State.Health?.Status;
            return health is not "unhealthy";
        }
        catch (DockerContainerNotFoundException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<ContainerStats> GetContainerStatsAsync(string containerId, CancellationToken ct = default)
    {
        ContainerInspectResponse inspect;
        try
        {
            inspect = await _client.Containers.InspectContainerAsync(containerId, ct);
        }
        catch (DockerContainerNotFoundException)
        {
            return new ContainerStats();
        }

        var isRunning = inspect.State?.Running == true && inspect.State.Restarting != true;
        var exitCode = inspect.State?.ExitCode ?? 0L;
        var uptimeSeconds = 0.0;

        if (isRunning &&
            DateTimeOffset.TryParse(
                inspect.State?.StartedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var startedAt) &&
            startedAt > DateTimeOffset.UnixEpoch)
        {
            uptimeSeconds = Math.Max(0.0, Math.Round((DateTimeOffset.UtcNow - startedAt).TotalSeconds, 2));
        }

        var result = new ContainerStats
        {
            IsRunning = isRunning,
            UptimeSeconds = uptimeSeconds,
            ExitCode = exitCode
        };

        if (!isRunning)
            return result;

        var statsProgress = new ContainerStatsProgress();
        try
        {
            await _client.Containers.GetContainerStatsAsync(
                containerId,
                new ContainerStatsParameters { Stream = false },
                statsProgress,
                ct);
        }
        catch (DockerContainerNotFoundException)
        {
            result.IsRunning = false;
            return result;
        }

        var stats = statsProgress.Latest;
        if (stats is null)
            return result;

        var rawMemoryBytes = stats.MemoryStats?.Usage ?? 0UL;
        if (stats.MemoryStats?.Stats is not null &&
            stats.MemoryStats.Stats.TryGetValue("inactive_file", out var inactiveFile) &&
            inactiveFile < rawMemoryBytes)
        {
            rawMemoryBytes -= inactiveFile;
        }

        result.MemoryUsageMiB = Math.Round(rawMemoryBytes / (1024.0 * 1024.0), 2);
        result.MemoryLimitMiB = Math.Round((stats.MemoryStats?.Limit ?? 0UL) / (1024.0 * 1024.0), 2);

        var cpuDelta = (double)(stats.CPUStats?.CPUUsage?.TotalUsage ?? 0UL) -
                       (double)(stats.PreCPUStats?.CPUUsage?.TotalUsage ?? 0UL);
        var systemDelta = (double)(stats.CPUStats?.SystemUsage ?? 0UL) -
                          (double)(stats.PreCPUStats?.SystemUsage ?? 0UL);
        var onlineCpus = stats.CPUStats?.OnlineCPUs ?? 0U;
        if (onlineCpus == 0 && stats.CPUStats?.CPUUsage?.PercpuUsage is { Count: > 0 } perCpu)
        {
            onlineCpus = (uint)perCpu.Count;
        }
        if (onlineCpus == 0)
        {
            onlineCpus = 1U;
        }

        if (cpuDelta > 0 && systemDelta > 0)
        {
            result.CpuUsagePercent = Math.Round((cpuDelta / systemDelta) * onlineCpus * 100.0, 2);
        }

        return result;
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
        catch (DockerApiException ex)
        {
            try
            {
                var container = await _client.Containers.InspectContainerAsync(containerId, ct);
                if (container.State is not null && !container.State.Running && !container.State.Restarting)
                {
                    _logger.LogWarning(
                        ex,
                        "Docker reported an error while stopping container '{ContainerId}', but the container has already exited.",
                        containerId);
                    return;
                }
            }
            catch (DockerContainerNotFoundException)
            {
                _logger.LogWarning(ex, "Container '{ContainerId}' was removed while stopping.", containerId);
                return;
            }

            throw;
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
    private sealed class DockerBuildProgress(ILogger<DockerService> logger, string moduleId) : IProgress<JSONMessage>
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

    /// <summary>
    /// Captures a single container stats snapshot from the Docker API.
    /// </summary>
    private sealed class ContainerStatsProgress : IProgress<ContainerStatsResponse>
    {
        /// <summary>
        /// Gets the most recently reported container stats response.
        /// </summary>
        public ContainerStatsResponse? Latest { get; private set; }

        /// <inheritdoc/>
        public void Report(ContainerStatsResponse value) => Latest = value;
    }
}