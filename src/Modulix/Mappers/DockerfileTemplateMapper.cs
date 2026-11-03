using Modulix.Database.Entities;

using Modulix.Client.Models.Dtos;

namespace Modulix.Mappers;

/// <summary>
/// Provides mapping methods for DockerfileTemplate entities and DTOs.
/// </summary>
public static class DockerfileTemplateMapper
{
    /// <summary>
    /// Maps a <see cref="DockerfileTemplate"/> entity to a <see cref="DockerfileTemplateDto"/>.
    /// </summary>
    /// <param name="entity">The <see cref="DockerfileTemplate"/> entity to map.</param>
    /// <returns>A <see cref="DockerfileTemplateDto"/> representing the entity.</returns>
    public static DockerfileTemplateDto ToDto(this DockerfileTemplate entity)
    {
        return new DockerfileTemplateDto
        {
            Id = entity.Id,
            FileName = entity.FileName,
            StoragePath = entity.StoragePath,
            Description = entity.Description,
            IsBuiltIn = entity.IsBuiltIn,
            CreatedAt = entity.CreatedAtUtc,
            UpdatedAt = entity.UpdatedAtUtc
        };
    }
}