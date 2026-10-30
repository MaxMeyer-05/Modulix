using System.Globalization;

using Microsoft.Extensions.Logging;

using Modulix.Docker.Models;
using Modulix.Docker.Extensions;
using Modulix.Docker.Services.Interfaces;

namespace Modulix.Docker.Services;

/// <inheritdoc cref="IModuleDockerService"/>
public class ModuleDockerService : IModuleDockerService
{
    /// <summary>
    /// The name of the Docker network used for module containers.
    /// </summary>
    private const string NetworkName = "modulix-network";

    /// <summary>
    /// The Docker service used for container operations.
    /// </summary>
    private readonly IDockerService _dockerService;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<ModuleDockerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleDockerService"/> class.
    /// </summary>
    /// <param name="dockerService">The Docker service used for container operations.</param>
    /// <param name="logger">The logger.</param>
    internal ModuleDockerService(
        IDockerService dockerService, 
        ILogger<ModuleDockerService> logger)
    {
        _dockerService = dockerService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> BuildModuleContainerAsync(
        Guid moduleId, 
        string storagePath, 
        string entryDllName, 
        int containerPort, 
        CancellationToken ct = default)
    {
        var dockerfilePath = Path.Combine(storagePath, "Dockerfile");

        if (!File.Exists(dockerfilePath))
        {
            var baseTemplatePath = Path.Combine(AppContext.BaseDirectory, "Resources", "Module", "Dockerfile");
            if (!File.Exists(baseTemplatePath))
            {
                throw new FileNotFoundException($"Base module Dockerfile template not found at '{baseTemplatePath}'.", baseTemplatePath);
            }

            var templateContent = await File.ReadAllTextAsync(baseTemplatePath, ct);
            var generatedDockerfile = templateContent.Replace("{{ENTRY_DLL}}", entryDllName);

            await File.WriteAllTextAsync(dockerfilePath, generatedDockerfile, ct);
            _logger.LogInformation("Generated Dockerfile for module '{ModuleId}' using base template.", moduleId);
        }

        var version = Guid.NewGuid().ToString("N");
        var imageName = $"modulix-module-{moduleId:N}:{version}";
        var containerName = $"modulix-container-{moduleId:N}-{version}";
        var hostDataPath = DockerExtension.EnsureHostDataDirectory(storagePath);
        var portString = containerPort.ToString(CultureInfo.InvariantCulture);

        var env = new List<string>
        {
            $"ASPNETCORE_HTTP_PORTS={portString}",
            "ASPNETCORE_ENVIRONMENT=Production"
        };

        var binds = new List<string>
        {
            $"{hostDataPath}:/app/data"
        };

        await _dockerService.EnsureNetworkExistsAsync(NetworkName, ct);
        return await _dockerService.BuildContainerAsync(storagePath, "Dockerfile", imageName, containerName, NetworkName, containerPort, env, binds, ct);
    }

    /// <inheritdoc/>
    public async Task<ContainerStats> GetModuleStatsAsync(string containerId, CancellationToken ct = default)
        => await _dockerService.GetContainerStatsAsync(containerId, ct);

    /// <inheritdoc/>
    public Task<bool> IsModuleRunningAsync(string containerId, CancellationToken ct = default)
        => _dockerService.IsContainerRunningAsync(containerId, ct);

    /// <inheritdoc/>
    public Task RemoveModuleContainerAsync(string containerId, Guid containerName, CancellationToken ct = default)
        => _dockerService.RemoveContainerAsync(containerId, containerName.ToString("N"), ct);

    /// <inheritdoc/>
    public Task RunModuleContainerAsync(string containerId, CancellationToken ct = default)
        => _dockerService.RunContainerAsync(containerId, ct);

    /// <inheritdoc/>
    public Task StopModuleContainerAsync(string containerId, CancellationToken ct = default)
        => _dockerService.StopContainerAsync(containerId, ct);

    /// <inheritdoc/>
    public Task<bool> TryRemoveModuleContainerAsync(string containerId, Guid containerName, CancellationToken ct = default)
        => _dockerService.TryRemoveModuleContainerAsync(containerId, containerName.ToString("N"), ct);

    /// <inheritdoc/>
    public Task WaitUntilReadyAsync(string containerId, CancellationToken ct = default)
        => _dockerService.WaitUntilReadyAsync(containerId, ct);
}