namespace Modulix.Docker.Services.Interfaces;

/// <summary>
/// Manages the container stack for Keycloak and its backing database.
/// </summary>
public interface IKeycloakDockerService
{
    /// <summary>
    /// Ensures that both Postgres and Keycloak containers exist and are started.
    /// </summary>
    Task EnsureKeycloakStackAsync(string authorityHost, CancellationToken ct = default);

    /// <summary>
    /// Polls the OIDC discovery endpoint until Keycloak is ready.
    /// </summary>
    Task<bool> WaitUntilReadyAsync(string authorityUrl, TimeSpan timeout, CancellationToken ct = default);

    /// <summary>
    /// Starts both containers if they are stopped.
    /// </summary>
    Task RunKeycloakStackAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops both Keycloak and PostgreSQL containers.
    /// </summary>
    Task StopKeycloakStackAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks whether the Keycloak container is running.
    /// </summary>
    Task<bool> IsKeycloakRunningAsync(CancellationToken ct = default);
}