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
    /// <param name="entity">The <see cref="ModuleVersion"/> entity to convert.</param>
    /// <returns>A <see cref="ModuleVersionDto"/> representing the entity.</returns>
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

    /// <summary>
    /// Converts a <see cref="ModuleVersionCreateDto"/> to a <see cref="ModuleVersion"/> entity.
    /// </summary>
    /// <param name="dto">The <see cref="ModuleVersionCreateDto"/> to convert.</param>
    /// <returns>A <see cref="ModuleVersion"/> entity representing the DTO.</returns>
    public static ModuleVersion ToVersionEntity(this ModuleVersionCreateDto dto)
    {
        return new ModuleVersion
        {
            Version = dto.Version.Trim(),
            Changelog = dto.Changelog?.Trim()
        };
    }

    /// <summary>
    /// Updates an existing <see cref="ModuleVersion"/> entity with values from a <see cref="ModuleVersionUpdateDto"/>.
    /// </summary>
    /// <param name="entity">The <see cref="ModuleVersion"/> entity to update.</param>
    /// <param name="dto">The <see cref="ModuleVersionUpdateDto"/> containing the updated values.</param>
    public static void UpdateVersionEntity(this ModuleVersion entity, ModuleVersionUpdateDto dto)
    {
        entity.Version = dto.Version?.Trim() ?? entity.Version;
        entity.Changelog = dto.Changelog?.Trim() ?? entity.Changelog;
    }
}