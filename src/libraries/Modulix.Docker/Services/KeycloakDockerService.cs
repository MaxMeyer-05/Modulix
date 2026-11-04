using Docker.DotNet.Models;

using System.Net;

using Microsoft.Extensions.Logging;

using Modulix.Docker.Services.Interfaces;

namespace Modulix.Docker.Services;

/// <inheritdoc cref="IKeycloakDockerService"/>
internal class KeycloakDockerService : IKeycloakDockerService
{
    /// <summary>
    /// The Docker image tag for the PostgreSQL database container.
    /// </summary>
    private const string PostgresImageTag = "modulix-postgres:latest";
    
    /// <summary>
    /// The Docker image tag for the Keycloak container.
    /// </summary>
    private const string KeycloakImageTag = "modulix-keycloak:latest";


    /// <summary>
    /// The name of the PostgreSQL database container.
    /// </summary>
    private const string DbContainerName = "keycloak-db";

    /// <summary>
    /// The name of the Keycloak container.
    /// </summary>
    private const string KeycloakContainerName = "keycloak";

    /// <summary>
    /// The name of the Docker network used for Keycloak and PostgreSQL containers.
    /// </summary>
    private const string NetworkName = "keycloak-net";

    /// <summary>
    /// The name of the Docker volume used for PostgreSQL data.
    /// </summary>
    private const string VolumeName = "postgres-data";


    /// <summary>
    /// The Docker service for managing Docker resources.
    /// </summary>
    private readonly IDockerService _dockerService;

    /// <summary>
    /// The HTTP client.
    /// </summary>
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<KeycloakDockerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeycloakDockerService"/> class.
    /// </summary>
    /// <param name="dockerService">The Docker service for managing Docker resources.</param>
    /// <param name="httpClientFactory">The HTTP client.</param>
    /// <param name="logger">The logger.</param>
    public KeycloakDockerService(
        IDockerService dockerService,
        IHttpClientFactory httpClientFactory,
        ILogger<KeycloakDockerService> logger)
    {
        _dockerService = dockerService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task EnsureKeycloakStackAsync(string authorityHost, CancellationToken ct = default)
    {
        await _dockerService.EnsureNetworkExistsAsync(NetworkName, ct);
        var resourcesPath = Path.Combine(AppContext.BaseDirectory, "Resources");
        await _dockerService.EnsureContainerRunningAsync(new CreateContainerParameters
        {
            Image = PostgresImageTag,
            Name = DbContainerName,
            HostConfig = new HostConfig
            {
                NetworkMode = NetworkName,
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.Always },
                Binds = [$"{VolumeName}:/var/lib/postgresql/data:Z"]
            }
        }, Path.Combine(resourcesPath, "Postgres"), ct: ct);

        await _dockerService.EnsureContainerRunningAsync(new CreateContainerParameters
        {
            Image = KeycloakImageTag,
            Name = KeycloakContainerName,
            ExposedPorts = new Dictionary<string, EmptyStruct> { ["8080/tcp"] = default },
            Env = [$"KC_HOSTNAME=http://{authorityHost}"],
            HostConfig = new HostConfig
            {
                NetworkMode = NetworkName,
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.Always },
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["8080/tcp"] = [new PortBinding { HostPort = "8080" }]
                }
            }
        }, Path.Combine(resourcesPath, "Keycloak"), ct: ct);
    }

    /// <inheritdoc/>
    public async Task<bool> IsKeycloakRunningAsync(CancellationToken ct = default)
    {
        return await _dockerService.IsContainerRunningByNameAsync(KeycloakContainerName, ct);
    }

    /// <inheritdoc/>
    public async Task RunKeycloakStackAsync(CancellationToken ct = default)
    {
        await _dockerService.StartContainerByNameAsync(DbContainerName, ct);
        await _dockerService.StartContainerByNameAsync(KeycloakContainerName, ct);
    }

    /// <inheritdoc/>
    public async Task StopKeycloakStackAsync(CancellationToken ct = default)
    {
        await _dockerService.StopContainerByNameAsync(KeycloakContainerName, ct);
        await _dockerService.StopContainerByNameAsync(DbContainerName, ct);
    }

    /// <inheritdoc/>
    public async Task<bool> WaitUntilReadyAsync(string authorityUrl, TimeSpan timeout, CancellationToken ct = default)
    {
        var discoveryUrl = $"{authorityUrl.TrimEnd('/')}/.well-known/openid-configuration";
        _logger.LogInformation("Waiting for Keycloak readiness at {Url}...", discoveryUrl);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);

        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(2);

        while (!linkedCts.Token.IsCancellationRequested)
        {
            try
            {
                var response = await httpClient.GetAsync(discoveryUrl, linkedCts.Token);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    _logger.LogInformation("Keycloak is ready and accepting authentication requests at {Url}", discoveryUrl);
                    return true;
                }

                _logger.LogDebug("Keycloak responded with status {StatusCode}, retrying...", response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Keycloak endpoint not reachable yet ({Message}), retrying...", ex.Message);
            }

            try
            {
                await Task.Delay(1500, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogWarning("Timed out after {Timeout}s waiting for Keycloak at {Url}", timeout.TotalSeconds, discoveryUrl);
        return false;
    }

}