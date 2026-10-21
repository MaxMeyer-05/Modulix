using Modulix.Client.Models.Dtos;

namespace Modulix.Services.Interfaces;

/// <summary>
/// Provides actions for managing modular containers, uploaded artifacts, and routing configurations.
/// </summary>
public interface IModuleService
{
    /// <summary>
    /// Creates a new module with the specified details.
    /// </summary>
    /// <param name="dto">The details of the module to create.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the module creation operation.</returns>
    /// <exception cref="ArgumentException">Thrown if the provided module details are invalid.</exception>
    Task<ModuleCreationResultDto> CreateModuleAsync(CreateModuleDto dto, CancellationToken ct = default);

    /// <summary>
    /// Starts a module with the specified details.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module to start.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated details of the module after starting.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the module with the specified ID does not exist.</exception>
    Task<ModuleDetailDto> StartModuleAsync(Guid moduleId, CancellationToken ct = default);

    /// <summary>
    /// Stops a module with the specified ID.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module to stop.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated details of the module after stopping.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the module with the specified ID does not exist.</exception>
    Task<ModuleDetailDto> StopModuleAsync(Guid moduleId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all modules.
    /// </summary>
    /// <param name="pagination">The pagination query parameters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A collection of all module details, paged according to the provided pagination parameters.</returns>
    Task<PagedResultDto<ModuleDto>> GetAllModulesAsync(PaginationQueryDto pagination, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the details of a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The details of the specified module.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the module with the specified ID does not exist.</exception>
    Task<ModuleDetailDto> GetModuleByIdAsync(Guid moduleId, CancellationToken ct = default);

    /// <summary>
    /// Updates metadata for an existing module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module to update.</param>
    /// <param name="dto">The updated module details.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Thrown if the module with the specified ID does not exist.</exception>
    Task UpdateModuleAsync(Guid moduleId, UpdateModuleDto dto, CancellationToken ct = default);

    /// <summary>
    /// Deletes a module, cleans up its storage files, and removes all associated records.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module to delete.</param>
    /// <param name="ct">The cancellation token.</param>
    Task DeleteModuleAsync(Guid moduleId, CancellationToken ct = default);
}