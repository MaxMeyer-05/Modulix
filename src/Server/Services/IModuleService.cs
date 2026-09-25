using Server.Models.Dtos;
using Server.Models.Enums;

namespace Server.Services;

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
    Task<ModuleCreationResultDto> CreateModuleAsync(CreateModuleDto dto, CancellationToken ct = default);

    /// <summary>
    /// Confirms the final list of endpoints for a module in PendingConfirmation state.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="dto">The confirmed endpoints for the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated details of the module.</returns>
    Task<ModuleDetailDto> ConfirmEndpointsAsync(Guid moduleId, ConfirmEndpointsDto dto, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all modules.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A collection of all module details.</returns>
    Task<IEnumerable<ModuleDto>> GetAllModulesAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves the details of a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The details of the specified module.</returns>
    Task<ModuleDetailDto> GetModuleByIdAsync(Guid moduleId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the current status of a specific module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The current status of the module.</returns>
    Task<ModuleDetailDto> GetModuleStatusAsync(Guid moduleId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all registered sub-endpoints for a specific module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A collection of all registered sub-endpoints for the specified module.</returns>   
    Task<IEnumerable<ModuleEndpointDto>> GetModuleSubEndpointsAsync(Guid moduleId, CancellationToken ct = default);

    /// <summary>
    /// Updates metadata for an existing module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module to update.</param>
    /// <param name="dto">The updated module details.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated details of the module.</returns>
    Task<ModuleDto> UpdateModuleAsync(Guid moduleId, UpdateModuleDto dto, CancellationToken ct = default);

    /// <summary>
    /// Updates the operational status of an existing module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module to update.</param>
    /// <param name="dto">The updated module status.</param>
    /// <param name="ct">The cancellation token.</param>
    Task UpdateModuleStatusAsync(Guid moduleId, UpdateModuleStatusDto dto, CancellationToken ct = default);

    /// <summary>
    /// Replaces the binary files of an existing module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module to update.</param>
    /// <param name="file">The new binary file for the module.</param>
    /// <param name="ct">The cancellation token.</param>
    Task UpdateModuleFilesAsync(Guid moduleId, IFormFile file, CancellationToken ct = default);

    /// <summary>
    /// Deletes a module, cleans up its storage files, and removes all associated records.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module to delete.</param>
    /// <param name="ct">The cancellation token.</param>
    Task DeleteModuleAsync(Guid moduleId, CancellationToken ct = default);
}