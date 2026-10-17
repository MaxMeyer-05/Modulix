using Microsoft.Extensions.DependencyInjection;

using Modulix.Docker.Services;

namespace Modulix.Docker;

/// <summary>
/// Provides extension methods for registering Docker services with the dependency injection container.
/// </summary>
public static class DockerRegistration
{
    /// <summary>
    /// Adds the Docker services to the specified service collection.
    /// </summary>
    /// <param name="services">The service collection to which the Docker services will be added.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddDockerServices(this IServiceCollection services)
    {
        services.AddSingleton<IDockerService, DockerService>();
        return services;
    }
    
}