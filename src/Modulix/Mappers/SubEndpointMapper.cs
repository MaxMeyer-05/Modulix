using Modulix.Client.Models.Dtos;

using Modulix.Database.Entities;

namespace Modulix.Mappers;

/// <summary>
/// Provides mapping methods for converting between SubEndpoint entities and their corresponding DTOs.
/// </summary>
public static class SubEndpointMapper
{
    /// <summary>
    /// Converts a <see cref="ModuleEndpoint"/> entity to a <see cref="ModuleSubEndpointsDetailDto"/>.
    /// </summary>
    /// <param name="endpoint">The <see cref="ModuleEndpoint"/> entity to be mapped.</param>
    /// <returns>The corresponding <see cref="ModuleSubEndpointsDetailDto"/>.</returns>
    public static ModuleSubEndpointsDetailDto ToModuleSubEndpointsDetailDto(this ModuleEndpoint endpoint) => new()
    {
        Id = endpoint.Id,
        HttpMethod = endpoint.HttpMethod,
        EndpointPath = endpoint.EndpointPath,
        Status = endpoint.Status,
        DiscoveredInVersion = endpoint.ModuleVersion?.Version ?? string.Empty,
        CreatedAtUtc = endpoint.CreatedAtUtc,
        ModuleName = endpoint.Module.ModuleName
    };

    /// <summary>
    /// Converts a <see cref="ModuleEndpoint"/> entity to a <see cref="ModuleSubEndpointDto"/>.
    /// </summary>
    /// <param name="endpoint">The <see cref="ModuleEndpoint"/> entity to be mapped.</param>
    /// <returns>The corresponding <see cref="ModuleSubEndpointDto"/>.</returns>
    public static ModuleSubEndpointDto ToSubEndpointDto(this ModuleEndpoint endpoint) => new()
    {
        Id = endpoint.Id,
        HttpMethod = endpoint.HttpMethod,
        EndpointPath = endpoint.EndpointPath,
        Status = endpoint.Status,
        DiscoveredInVersion = endpoint.ModuleVersion?.Version ?? string.Empty,
        CreatedAtUtc = endpoint.CreatedAtUtc
    };
}
