using Modulix.Docker.Models;

namespace Modulix.Docker.Services.Interfaces;

/// <summary>
/// Provides functionality for building and preparing Docker containers for modules.
/// </summary>
public interface IModuleDockerService
{
    /// <summary>
    /// Builds a Docker container for the specified module using an existing or generated Dockerfile.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="storagePath">The path to the module's storage directory.</param>
    /// <param name="entryDllName">The name of the entry DLL for the module.</param>
    /// <param name="containerPort">The port to expose from the container.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The ID of the created Docker container.</returns>
    Task<string> BuildModuleContainerAsync(Guid moduleId, string storagePath, string entryDllName, int containerPort, CancellationToken ct = default);

    /// <summary>
    /// Starts the module container.
    /// </summary>
    /// <param name="containerId">The ID of the container to start.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RunModuleContainerAsync(string containerId, CancellationToken ct = default);

    /// <summary>
    /// Probes the module container until its port accepts connections or healthcheck passes.
    /// </summary>
    /// <param name="containerId">The ID of the container to wait for readiness.</param>
    /// <param name="ct">A cancellation token.</param>
    Task WaitUntilReadyAsync(string containerId, CancellationToken ct = default);

    /// <summary>
    /// Stops a running module container.
    /// </summary>
    /// <param name="containerId">The ID of the container to stop.</param>
    /// <param name="ct">A cancellation token.</param>
    Task StopModuleContainerAsync(string containerId, CancellationToken ct = default);

    /// <summary>
    /// Stops and removes the module container and deletes its custom built image.
    /// </summary>
    /// <param name="containerId">The ID of the container to remove.</param>
    /// <param name="containerName">The GUID of the container to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RemoveModuleContainerAsync(string containerId, Guid containerName, CancellationToken ct = default);

    /// <summary>
    /// Safely attempts to remove a module container without throwing exceptions.
    /// </summary>
    /// <param name="containerId">The ID of the container to remove.</param>
    /// <param name="containerName">The GUID of the container to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<bool> TryRemoveModuleContainerAsync(string containerId, Guid containerName, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the module container is actively running and not in a failed state.
    /// </summary>
    /// <param name="containerId">The ID of the container to check.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<bool> IsModuleRunningAsync(string containerId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves live CPU, Memory, and Uptime metrics for the module container.
    /// </summary>
    /// <param name="containerId">The ID of the container to retrieve stats for.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<ContainerStats> GetModuleStatsAsync(string containerId, CancellationToken ct = default);
}