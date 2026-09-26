using Server.Models.Dtos;
using Server.Database.Entities;

namespace Server.Mappers;

/// <summary>
/// Provides mapping methods for converting between
/// <see cref="Module"/> entities and <see cref="ModuleDto"/> objects.
/// </summary>
public static class ModuleMapper
{
    /// <summary>
    /// Maps a <see cref="Module"/> entity to its corresponding <see cref="ModuleDto"/>.
    /// </summary>
    /// <param name="module">The <see cref="Module"/> entity to be mapped.</param>
    /// <returns>The corresponding <see cref="ModuleDto"/>.</returns>
    public static ModuleDto ToModuleDto(this Module module) => new()
    {
        Id = module.Id,
        ModuleName = module.ModuleName,
        Description = module.Description,
        BaseEndpointPath = module.BaseEndpointPath,
        ContainerPort = module.ContainerPort,
        Status = module.Status,
        CreatedAtUtc = module.CreatedAtUtc
    };

    /// <summary>
    /// Maps a <see cref="Module"/> entity to its corresponding <see cref="ModuleDetailDto"/>.
    /// </summary>
    /// <param name="module">The <see cref="Module"/> entity to be mapped.</param>
    /// <returns>The corresponding <see cref="ModuleDetailDto"/>.</returns>
    public static ModuleDetailDto ToModuleDetailDto(this Module module) => new()
    {
        Id = module.Id,
        ModuleName = module.ModuleName,
        Description = module.Description,
        BaseEndpointPath = module.BaseEndpointPath,
        ContainerId = module.ContainerId,
        ContainerPort = module.ContainerPort,
        Status = module.Status,
        StoragePath = module.StoragePath,
        CreatedAtUtc = module.CreatedAtUtc,
        SubEndpoints = module.SubEndpoints.Select(e => e.ToEndpointDto()).ToList()
    };

    /// <summary>
    /// Converts a <see cref="ModuleEndpoint"/> entity to a <see cref="ModuleEndpointDto"/>.
    /// </summary>
    /// <param name="endpoint">The <see cref="ModuleEndpoint"/> entity to be mapped.</param>
    /// <returns>The corresponding <see cref="ModuleEndpointDto"/>.</returns>
    public static ModuleEndpointDto ToEndpointDto(this ModuleEndpoint endpoint) => new()
    {
        Id = endpoint.Id,
        HttpMethod = endpoint.HttpMethod,
        EndpointPath = endpoint.EndpointPath,
        Status = endpoint.Status,
        CreatedAtUtc = endpoint.CreatedAtUtc
    };

    /// <summary>
    /// Converts a <see cref="CreateModuleDto"/> to a <see cref="Module"/> entity.
    /// </summary>
    /// <param name="moduleDto">The DTO containing the module information.</param>
    /// <returns>The corresponding <see cref="Module"/> entity.</returns>
    public static Module ToModuleEntity(this CreateModuleDto moduleDto) => new()
    {
        ModuleName = moduleDto.ModuleName.Trim(),
        Description = moduleDto.Description?.Trim() ?? string.Empty
    };

    /// <summary>
    /// Updates a <see cref="Module"/> entity with the values from an <see cref="UpdateModuleDto"/>.
    /// </summary>
    /// <param name="module">The module entity to update.</param>
    /// <param name="moduleDto">The DTO containing the updated module information.</param>
    public static void UpdateModuleDto(this Module module, UpdateModuleDto moduleDto)
    {
        module.ModuleName = moduleDto.ModuleName?.Trim() ?? module.ModuleName;
        module.Description = moduleDto.Description?.Trim() ?? module.Description;
    }
}