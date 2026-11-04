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
            CreatedAt = entity.CreatedAtUtc,
            UpdatedAt = entity.UpdatedAtUtc
        };
    }

    /// <summary>
    /// Maps a <see cref="CreateDockerfileTemplateDto"/> to a <see cref="DockerfileTemplate"/> entity.
    /// </summary>
    /// <param name="dto">The DTO to map.</param>
    /// <returns>A <see cref="DockerfileTemplate"/> entity representing the DTO.</returns>
    public static DockerfileTemplate ToEntity(this CreateDockerfileTemplateDto dto)
    {
        return new DockerfileTemplate
        {
            FileName = dto.FileName,
            Description = dto.Description
        };
    }
}