using Docker.DotNet;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Modulix.Docker.Extensions;

using Modulix.Docker.Services;
using Modulix.Docker.Services.Interfaces;

namespace Modulix.Docker;

/// <summary>
/// Provides extension methods for registering Docker services with the dependency injection container.
/// </summary>
public static class DockerRegistration
{
    /// <summary>
    /// Adds the Docker client and Docker services to the specified service collection.
    /// </summary>
    /// <param name="services">The service collection to which the Docker services will be added.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddDockerServices(this IServiceCollection services)
    {
        services.TryAddSingleton<DockerClient>(sp =>
        {
            var options = sp.GetService<IOptions<DockerSettings>>()?.Value;
            var dockerUri = !string.IsNullOrWhiteSpace(options?.SocketUri)
                ? new Uri(options.SocketUri.Trim())
                : OperatingSystem.IsWindows()
                    ? new Uri("npipe://./pipe/docker_engine")
                    : new Uri("unix:///var/run/docker.sock");
            return new DockerClientConfiguration(dockerUri).CreateClient();
        });

        services.AddHttpClient();
        services.TryAddSingleton<DockerExtension>();

        services.AddSingleton<IDockerService, DockerService>();
        services.AddSingleton<IModuleDockerService, ModuleDockerService>();
        services.AddSingleton<IKeycloakDockerService, KeycloakDockerService>();

        return services;
    }
}