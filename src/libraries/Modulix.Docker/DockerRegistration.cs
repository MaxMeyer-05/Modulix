using Docker.DotNet;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Modulix.Docker.Services;

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
        services.TryAddSingleton<DockerClient>(_ =>
        {
            var dockerUri = OperatingSystem.IsWindows()
                ? new Uri("npipe://./pipe/docker_engine")
                : new Uri("unix:///var/run/docker.sock");

            return new DockerClientConfiguration(dockerUri).CreateClient();
        });

        services.AddSingleton<IDockerService, DockerService>();
        return services;
    }
}