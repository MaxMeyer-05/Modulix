using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

using Modulix.Database.Entities;
using Modulix.ReverseProxy.Models;

namespace Modulix.Mappers;

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
        BaseUrlPath = module.BaseUrlPath,
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
        BaseUrlPath = module.BaseUrlPath,
        ContainerId = module.ContainerId,
        ContainerPort = module.ContainerPort,
        Status = module.Status,
        CurrentVersion = module.CurrentVersion,
        CreatedAtUtc = module.CreatedAtUtc,
        Endpoints = module.SubEndpoints.Select(e => e.ToSubEndpointDto()).ToList(),
        Versions = module.Versions.Select(v => v.ToVersionDto()).ToList()
    };

    /// <summary>
    /// Converts a <see cref="CreateModuleDto"/> to a <see cref="Module"/> entity.
    /// </summary>
    /// <param name="moduleDto">The DTO containing the module information.</param>
    /// <returns>The corresponding <see cref="Module"/> entity.</returns>
    public static Module ToModuleEntity(this CreateModuleDto moduleDto) => new()
    {
        ModuleName = moduleDto.ModuleName.Trim(),
        Description = moduleDto.Description?.Trim() ?? string.Empty,
        CurrentVersion = moduleDto.Version.Trim()
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

    /// <summary>
    /// Maps a <see cref="Module"/> entity and its active sub-endpoints to a <see cref="ProxyModuleDefinition"/>.
    /// </summary>
    /// <param name="module">The <see cref="Module"/> entity to be mapped.</param>
    /// <returns>The corresponding <see cref="ProxyModuleDefinition"/>.</returns>
    public static ProxyModuleDefinition ToProxyModuleDefinition(this Module module) => new()
    {
        ModuleId = module.Id,
        BaseUrlPath = module.BaseUrlPath,
        ContainerPort = module.ContainerPort,
        Endpoints = module.SubEndpoints
            .Where(e => e.Status == ModuleEndpointsStatus.Active)
            .Select(e => new ProxyEndpointDefinition
            {
                HttpMethod = e.HttpMethod.Method,
                EndpointPath = e.EndpointPath
            })
            .ToList()
    };
}