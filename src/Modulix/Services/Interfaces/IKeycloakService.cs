namespace Modulix.Services.Interfaces;

/// <summary>
/// Provides high-level lifecycle and readiness management for the local Keycloak identity provider stack.
/// </summary>
public interface IKeycloakService
{
    /// <summary>
    /// Gets a value indicating whether the Keycloak identity provider is ready and accepting requests.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Gets the failure reason if Keycloak initialization failed.
    /// </summary>
    string? FailureReason { get; }

    /// <summary>
    /// Ensures that the Keycloak stack (PostgreSQL + Keycloak) is provisioned, started, and healthy.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    Task EnsureKeycloakStackAsync(CancellationToken ct = default);
}