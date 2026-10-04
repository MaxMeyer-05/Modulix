namespace Server.Services.Interfaces;

/// <summary>
/// Provides orchestration capabilities for building and managing Docker containers for modules.
/// </summary>
public interface IDockerService
{
    /// <summary>
    /// Builds the Docker image from the module directory and creates the container in the internal network.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="storagePath">The host directory path where the module files reside.</param>
    /// <param name="entryDllName">The name of the entry assembly DLL.</param>
    /// <param name="containerPort">The internal port the module listens on.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The ID of the created Docker container.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the Dockerfile is not found in the specified storage path.</exception>
    Task<string> BuildContainerAsync(Guid moduleId, string storagePath, string entryDllName, int containerPort, CancellationToken ct = default);

    /// <summary>
    /// Starts an existing Docker container.
    /// </summary>
    /// <param name="containerId">The ID of the container to start.</param>
    /// <param name="ct">The cancellation token.</param>
    Task RunContainerAsync(string containerId, CancellationToken ct = default);

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
    /// <param name="moduleId">The module identifier used to delete tagged images.</param>
    /// <param name="ct">The cancellation token.</param>
    Task RemoveContainerAsync(string containerId, Guid moduleId, CancellationToken ct = default);
}