using Modulix.Client.Models.Dtos;

namespace Modulix.Services.Interfaces;

/// <summary>
/// Defines the contract for reusable Dockerfile template operations.
/// </summary>
public interface IDockerfileTemplateService
{
    /// <summary>
    /// Creates a new Dockerfile template with the specified data.
    /// </summary>
    /// <param name="createDockerfileTemplateDto">The data required to create the Dockerfile template.</param>
    /// <param name="ct">The cancellation token.</param>
    Task CreateDockerfileTemplateAsync(CreateDockerfileTemplateDto createDockerfileTemplateDto, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all Dockerfile templates.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A list of Dockerfile template DTOs.</returns>
    Task<IList<DockerfileTemplateDto>> GetAllDockerfileTemplatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific Dockerfile template by its ID.
    /// </summary>
    /// <param name="id">The ID of the Dockerfile template.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The Dockerfile template DTO.</returns>
    Task<DockerfileTemplateDto> GetDockerfileTemplateAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing Dockerfile template with the specified data.
    /// </summary>
    /// <param name="updateDockerfileTemplateDto">The data required to update the Dockerfile template.</param>
    /// <param name="ct">The cancellation token.</param>
    Task UpdateDockerfileTemplateAsync(UpdateDockerfileTemplateDto updateDockerfileTemplateDto, CancellationToken ct = default);

    /// <summary>
    /// Deletes a Dockerfile template by its ID.
    /// </summary>
    /// <param name="id">The ID of the Dockerfile template to delete.</param>
    /// <param name="ct">The cancellation token.</param>
    Task DeleteDockerfileTemplateAsync(int id, CancellationToken ct = default);
}