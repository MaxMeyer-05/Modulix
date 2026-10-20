using Modulix.Client.Models.Dtos;

using Modulix.Database.Entities;

namespace Modulix.Mappers;

/// <summary>
/// Provides mapping methods for converting between ModuleVersion entities and ModuleVersionDto objects.
/// </summary>
public static class ModuleVersionMapper
{
    /// <summary>
    /// Converts a <see cref="ModuleVersion"/> entity to a <see cref="ModuleVersionDto"/>.
    /// </summary>
    /// <param name="entity">The ModuleVersion entity to convert.</param>
    /// <returns>A ModuleVersionDto representing the entity.</returns>
    public static ModuleVersionDto ToVersionDto(this ModuleVersion entity)
    {
        return new ModuleVersionDto
        {
            Id = entity.Id,
            Version = entity.Version,
            Changelog = entity.Changelog,
            StoragePath = entity.StoragePath,
            ReleasedAtUtc = entity.ReleasedAtUtc
        };
    }
}