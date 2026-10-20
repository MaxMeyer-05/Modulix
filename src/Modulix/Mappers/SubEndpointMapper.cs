using Modulix.Client.Models.Dtos;

using Modulix.Database.Entities;

namespace Modulix.Mappers;

/// <summary>
/// Provides mapping methods for converting between SubEndpoint entities and their corresponding DTOs.
/// </summary>
public static class SubEndpointMapper
{
    /// <summary>
    /// Converts a <see cref="ModuleEndpoint"/> entity to a <see cref="ModuleEndpointsDetailDto"/>.
    /// </summary>
    /// <param name="endpoint">The <see cref="ModuleEndpoint"/> entity to be mapped.</param>
    /// <returns>The corresponding <see cref="ModuleEndpointsDetailDto"/>.</returns>
    public static ModuleEndpointsDetailDto ToModuleSubEndpointsDetailDto(this ModuleEndpoint endpoint) => new()
    {
        Id = endpoint.Id,
        HttpMethod = endpoint.HttpMethod,
        EndpointPath = endpoint.EndpointPath,
        Status = endpoint.Status,
        CreatedAtUtc = endpoint.CreatedAtUtc,
        ModuleName = endpoint.Module.ModuleName
    };

    /// <summary>
    /// Converts a <see cref="ModuleEndpoint"/> entity to a <see cref="ModuleEndpointDto"/>.
    /// </summary>
    /// <param name="endpoint">The <see cref="ModuleEndpoint"/> entity to be mapped.</param>
    /// <returns>The corresponding <see cref="ModuleEndpointDto"/>.</returns>
    public static ModuleEndpointDto ToSubEndpointDto(this ModuleEndpoint endpoint) => new()
    {
        Id = endpoint.Id,
        HttpMethod = endpoint.HttpMethod,
        EndpointPath = endpoint.EndpointPath,
        Status = endpoint.Status,
        CreatedAtUtc = endpoint.CreatedAtUtc
    };
}
