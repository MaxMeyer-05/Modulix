using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

using Microsoft.Extensions.DependencyInjection;

using Yarp.ReverseProxy.Configuration;

using Modulix.ReverseProxy.Services;

namespace Modulix.ReverseProxy;

/// <summary>
/// Provides extension methods for registering the Modulix reverse proxy services and endpoints.
/// </summary>
public static class ReverseProxyRegistration
{
    /// <summary>
    /// Adds the Modulix reverse proxy services and YARP reverse proxy to the specified service collection.
    /// </summary>
    /// <param name="services">The service collection to add the reverse proxy services to.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddReverseProxyServices(this IServiceCollection services)
    {
        services.AddSingleton<ReverseProxyService>();
        services.AddSingleton<IReverseProxyService>(sp => sp.GetRequiredService<ReverseProxyService>());
        services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<ReverseProxyService>());
        services.AddReverseProxy();

        return services;
    }

    /// <summary>
    /// Maps the Modulix reverse proxy endpoints onto the ASP.NET Core routing pipeline.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to map the reverse proxy routes onto.</param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> that can be used to further customize the reverse proxy endpoints.</returns>
    public static IEndpointConventionBuilder MapModulixReverseProxy(this IEndpointRouteBuilder endpoints)
        => endpoints.MapReverseProxy();
}
