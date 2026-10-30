using Microsoft.Extensions.Options;
using Modulix.Docker.Services.Interfaces;
using Modulix.Services.Interfaces;

namespace Modulix.Services;

/// <inheritdoc cref="IKeycloakService"/>
public class KeycloakService : BackgroundService, IKeycloakService
{
    private readonly IKeycloakDockerService _keycloakDockerService;
    private readonly AuthorizationSettings _authSettings;
    private readonly ILogger<KeycloakService> _logger;

    private volatile bool _isReady;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeycloakService"/> class.
    /// </summary>
    public KeycloakService(
        IKeycloakDockerService keycloakDockerService,
        IOptions<AuthorizationSettings> authOptions,
        ILogger<KeycloakService> logger)
    {
        _keycloakDockerService = keycloakDockerService;
        _authSettings = authOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsReady => _isReady;

    /// <inheritdoc/>
    public string? FailureReason { get; private set; }

    /// <inheritdoc/>
    public async Task EnsureKeycloakStackAsync(CancellationToken ct = default)
    {
        if (!_authSettings.AutoProvisionContainer)
        {
            _logger.LogInformation("AutoProvisionContainer is false. Skipping Keycloak provisioning.");
            _isReady = true;
            return;
        }

        try
        {
            var authorityUri = new Uri(_authSettings.Authority);
            var authorityHost = authorityUri.Authority;

            _logger.LogInformation("Provisioning Keycloak stack for authority '{Authority}' (Host: {Host})...", 
                _authSettings.Authority, authorityHost);

            await _keycloakDockerService.EnsureKeycloakStackAsync(authorityHost, ct);

            var ready = await _keycloakDockerService.WaitUntilReadyAsync(
                _authSettings.Authority, 
                TimeSpan.FromMinutes(2), 
                ct);

            if (ready)
            {
                _isReady = true;
                _logger.LogInformation("Keycloak identity provider is ready and accepting authentication requests.");
            }
            else
            {
                FailureReason = "Timed out waiting for Keycloak readiness.";
                _logger.LogError("Keycloak failed to become ready within the allocated timeout.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            
        }
        catch (Exception ex)
        {
            FailureReason = ex.Message;
            _logger.LogError(ex, "An error occurred while provisioning the Keycloak stack.");
        }
    }

    /// <summary>
    /// Runs the asynchronous Keycloak initialization loop in the background on application startup.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_authSettings.AutoProvisionContainer)
        {
            _isReady = true;
            return;
        }

        await EnsureKeycloakStackAsync(stoppingToken);
    }

    /// <summary>
    /// Gracefully stops the Keycloak stack when the server application shuts down.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Shutting down Keycloak background service...");
        await base.StopAsync(cancellationToken);
    }
}