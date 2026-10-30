using Docker.DotNet;
using Docker.DotNet.Models;

using System.Net;
using System.Formats.Tar;

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
    /// The Docker client used to interact with the Docker daemon.
    /// </summary>
    private readonly DockerClient _client;

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
    /// <param name="client">The Docker client used to interact with the Docker daemon.</param>
    /// <param name="dockerService">The Docker service for managing Docker resources.</param>
    /// <param name="httpClientFactory">The HTTP client.</param>
    /// <param name="logger">The logger.</param>
    public KeycloakDockerService(
        DockerClient client,
        IDockerService dockerService,
        IHttpClientFactory httpClientFactory,
        ILogger<KeycloakDockerService> logger)
    {
        _client = client;
        _dockerService = dockerService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task EnsureKeycloakStackAsync(string authorityHost, CancellationToken ct = default)
    {
        await _dockerService.EnsureNetworkExistsAsync(NetworkName, ct);
        await EnsurePostgresRunningAsync(ct);
        await EnsureKeycloakRunningAsync(authorityHost, ct);
    }

    /// <inheritdoc/>
    public async Task<bool> IsKeycloakRunningAsync(CancellationToken ct = default)
    {
        var container = await FindContainerAsync(KeycloakContainerName, ct);
        return container?.State == "running";
    }

    /// <inheritdoc/>
    public async Task RunKeycloakStackAsync(CancellationToken ct = default)
    {
        await StartContainerByNameAsync(DbContainerName, ct);
        await StartContainerByNameAsync(KeycloakContainerName, ct);
    }

    /// <inheritdoc/>
    public async Task StopKeycloakStackAsync(CancellationToken ct = default)
    {
        await StopContainerByNameAsync(KeycloakContainerName, ct);
        await StopContainerByNameAsync(DbContainerName, ct);
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
                    _logger.LogInformation("Keycloak is ready to issue tokens at {Url}", discoveryUrl);
                    return true;
                }
            }
            catch
            {
                _logger.LogInformation("Keycloak is not ready yet at {Url}", discoveryUrl);
            }

            await Task.Delay(1500, linkedCts.Token);
        }

        return false;
    }

    private async Task EnsurePostgresRunningAsync(CancellationToken ct)
    {
        var container = await FindContainerAsync(DbContainerName, ct);

        if (container == null)
        {
            await BuildPostgresImageIfMissingAsync(ct);

            _logger.LogInformation("Creating container '{Name}' from image '{Image}'...", DbContainerName, PostgresImageTag);
            
            var response = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = PostgresImageTag,
                Name = DbContainerName,
                HostConfig = new HostConfig
                {
                    NetworkMode = NetworkName,
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.Always },
                    Binds = [$"{VolumeName}:/var/lib/postgresql/data:Z"]
                }
            }, ct);

            await _client.Containers.StartContainerAsync(response.ID, new ContainerStartParameters(), ct);
            _logger.LogInformation("Container '{Name}' created and started.", DbContainerName);
        }
        else if (container.State != "running")
        {
            _logger.LogInformation("Starting existing container '{Name}'...", DbContainerName);
            await _client.Containers.StartContainerAsync(container.ID, new ContainerStartParameters(), ct);
        }
    }

    private async Task EnsureKeycloakRunningAsync(string authorityHost, CancellationToken ct)
    {
        var container = await FindContainerAsync(KeycloakContainerName, ct);

        if (container == null)
        {
            await BuildKeycloakImageIfMissingAsync(ct);

            _logger.LogInformation("Creating container '{Name}' with KC_HOSTNAME='{Host}'...", KeycloakContainerName, authorityHost);

            var response = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = KeycloakImageTag,
                Name = KeycloakContainerName,
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    ["8080/tcp"] = default
                },
                Env =
                [
                    $"KC_HOSTNAME=http://{authorityHost}"
                ],
                HostConfig = new HostConfig
                {
                    NetworkMode = NetworkName,
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.Always },
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        ["8080/tcp"] = [new PortBinding { HostPort = "8080" }]
                    }
                }
            }, ct);

            await _client.Containers.StartContainerAsync(response.ID, new ContainerStartParameters(), ct);
        }
        else if (container.State != "running")
        {
            await _client.Containers.StartContainerAsync(container.ID, new ContainerStartParameters(), ct);
        }
    }

    private async Task BuildKeycloakImageIfMissingAsync(CancellationToken ct)
    {
        var images = await _client.Images.ListImagesAsync(new ImagesListParameters(), ct);
        if (images.Any(i => i.RepoTags != null && i.RepoTags.Contains(KeycloakImageTag)))
        {
            return;
        }

        var buildContextPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Keycloak");
        if (!Directory.Exists(buildContextPath))
        {
            throw new DirectoryNotFoundException($"Keycloak build context directory not found: '{buildContextPath}'");
        }

        _logger.LogInformation("Building image '{Image}' from '{Context}'...", KeycloakImageTag, buildContextPath);

        await using var tarStream = new MemoryStream();
        TarFile.CreateFromDirectory(buildContextPath, tarStream, includeBaseDirectory: false);
        tarStream.Position = 0;

        await _client.Images.BuildImageFromDockerfileAsync(
            new ImageBuildParameters
            {
                Tags = [KeycloakImageTag],
                Dockerfile = "Dockerfile"
            },
            tarStream,
            [],
            new Dictionary<string, string>(),
            new Progress<JSONMessage>(),
            ct);
    }

    private async Task BuildPostgresImageIfMissingAsync(CancellationToken ct)
    {
        var images = await _client.Images.ListImagesAsync(new ImagesListParameters(), ct);
        if (images.Any(i => i.RepoTags != null && i.RepoTags.Contains(PostgresImageTag)))
        {
            return;
        }

        var buildContextPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Postgres");
        if (!Directory.Exists(buildContextPath))
        {
            throw new DirectoryNotFoundException($"Postgres build context directory not found: '{buildContextPath}'");
        }

        _logger.LogInformation("Building image '{Image}' from '{Context}'...", PostgresImageTag, buildContextPath);

        await using var tarStream = new MemoryStream();
        TarFile.CreateFromDirectory(buildContextPath, tarStream, includeBaseDirectory: false);
        tarStream.Position = 0;

        await _client.Images.BuildImageFromDockerfileAsync(
            new ImageBuildParameters
            {
                Tags = [PostgresImageTag],
                Dockerfile = "Dockerfile"
            },
            tarStream,
            [],
            new Dictionary<string, string>(),
            new Progress<JSONMessage>(),
            ct);

        _logger.LogInformation("Image '{Image}' built successfully.", PostgresImageTag);
    }

    private async Task<ContainerListResponse?> FindContainerAsync(string name, CancellationToken ct)
    {
        var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [name] = true }
            }
        }, ct);

        return containers.FirstOrDefault(c =>
            c.Names.Any(n => n.TrimStart('/').Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task StartContainerByNameAsync(string name, CancellationToken ct)
    {
        var container = await FindContainerAsync(name, ct);
        if (container != null && container.State != "running")
        {
            await _client.Containers.StartContainerAsync(container.ID, new ContainerStartParameters(), ct);
        }
    }

    private async Task StopContainerByNameAsync(string name, CancellationToken ct)
    {
        var container = await FindContainerAsync(name, ct);
        if (container != null && container.State == "running")
        {
            await _client.Containers.StopContainerAsync(container.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 5 }, ct);
        }
    }
}