using Docker.DotNet.Models;

using Modulix.Docker.Models;

namespace Modulix.Docker.Services.Interfaces;

/// <summary>
/// Provides common operations for building and managing Docker resources.
/// </summary>
internal interface IDockerService
{
    /// <summary>
    /// Builds a Docker container from the specified build context and Dockerfile, and configures it with the given parameters.
    /// </summary>
    /// <param name="buildContextPath">The path to the Docker build context.</param>
    /// <param name="dockerfileName">The name of the Dockerfile to use.</param>
    /// <param name="imageName">The name of the Docker image to build.</param>
    /// <param name="containerName">The name of the Docker container to create.</param>
    /// <param name="networkName">The name of the Docker network to connect the container to.</param>
    /// <param name="containerPort">The internal port the container will expose.</param>
    /// <param name="env">Optional environment variables to set in the container.</param>
    /// <param name="binds">Optional volume bindings for the container.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The ID of the created Docker container.</returns>
    Task<string> BuildContainerAsync(
        string buildContextPath, 
        string dockerfileName, 
        string imageName, 
        string containerName, 
        string networkName,
        int containerPort, 
        IList<string>? env = null, 
        IList<string>? binds = null, 
        CancellationToken ct = default);

    /// <summary>
    /// Ensures that the specified Docker network exists, creating it if necessary.
    /// </summary>
    /// <param name="networkName">The name of the Docker network.</param>
    /// <param name="ct">The cancellation token.</param>
    Task EnsureNetworkExistsAsync(string networkName, CancellationToken ct = default);

    /// <summary>
    /// Builds the specified Docker image if it is not already available.
    /// </summary>
    /// <param name="imageTag">The image tag.</param>
    /// <param name="buildContextPath">The Docker build context.</param>
    /// <param name="dockerfileName">The Dockerfile name.</param>
    /// <param name="ct">The cancellation token.</param>
    Task EnsureImageExistsAsync(string imageTag, string buildContextPath, string dockerfileName = "Dockerfile", CancellationToken ct = default);

    /// <summary>
    /// Ensures a named Docker container exists and is running, building its image if needed.
    /// </summary>
    /// <param name="parameters">The Docker container configuration.</param>
    /// <param name="buildContextPath">The context used to build the configured image if it is missing.</param>
    /// <param name="dockerfileName">The Dockerfile in the build context.</param>
    /// <param name="ct">The cancellation token.</param>
    Task EnsureContainerRunningAsync(CreateContainerParameters parameters, string buildContextPath, string dockerfileName = "Dockerfile", CancellationToken ct = default);

    /// <summary>
    /// Checks whether a container with the specified name is running.
    /// </summary>
    /// <param name="containerName">The container name.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True when the container exists and is running.</returns>
    Task<bool> IsContainerRunningByNameAsync(string containerName, CancellationToken ct = default);

    /// <summary>
    /// Starts a named container if it exists and is stopped.
    /// </summary>
    /// <param name="containerName">The container name.</param>
    /// <param name="ct">The cancellation token.</param>
    Task StartContainerByNameAsync(string containerName, CancellationToken ct = default);

    /// <summary>
    /// Stops a named container if it exists and is running.
    /// </summary>
    /// <param name="containerName">The container name.</param>
    /// <param name="ct">The cancellation token.</param>
    Task StopContainerByNameAsync(string containerName, CancellationToken ct = default);

    /// <summary>
    /// Starts an existing Docker container.
    /// </summary>
    /// <param name="containerId">The ID of the container to start.</param>
    /// <param name="ct">The cancellation token.</param>
    Task RunContainerAsync(string containerId, CancellationToken ct = default);

    /// <summary>
    /// Waits for a running container to pass its port health check, with a bounded timeout.
    /// </summary>
    /// <param name="containerId">The ID of the container to check.</param>
    /// <param name="ct">The cancellation token.</param>
    Task WaitUntilReadyAsync(string containerId, CancellationToken ct = default);

    /// <summary>
    /// Stops a running Docker container.
    /// </summary>
    /// <param name="containerId">The ID of the container to stop.</param>
    /// <param name="ct">The cancellation token.</param>
    Task StopContainerAsync(string containerId, CancellationToken ct = default);

    /// <summary>
    /// Stops, removes the container, and deletes the built image to free disk space.
    /// </summary>
    /// <param name="containerId">The ID of the container to remove.</param>
    /// <param name="containerName">The name of the container used to delete tagged images.</param>
    /// <param name="ct">The cancellation token.</param>
    Task RemoveContainerAsync(string containerId, string containerName, CancellationToken ct = default);

    /// <summary>
    /// Attempts to remove a Docker container without throwing if cleanup fails.
    /// </summary>
    /// <param name="containerId">The ID of the container to remove.</param>
    /// <param name="containerName">The name of the container.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True if the container was successfully removed; otherwise, false.</returns>
    Task<bool> TryRemoveContainerAsync(string containerId, string containerName, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the specified container is running and healthy.
    /// </summary>
    /// <param name="containerId">The ID of the container to inspect.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True if the container is running and has not failed; otherwise, false.</returns>
    Task<bool> IsContainerRunningAsync(string containerId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single snapshot of CPU, memory, uptime, and exit code metrics for the specified container.
    /// </summary>
    /// <param name="containerId">The ID of the container to inspect.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The current container resource statistics and live state.</returns>
    Task<ContainerStats> GetContainerStatsAsync(string containerId, CancellationToken ct = default);
}