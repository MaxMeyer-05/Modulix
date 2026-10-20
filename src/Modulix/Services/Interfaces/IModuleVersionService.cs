using Modulix.Client.Models.Dtos;

namespace Modulix.Services.Interfaces;

/// <summary>
/// Defines the interface for module version management services within the Modulix application.
/// </summary>
public interface IModuleVersionService
{
    /// <summary>
    /// Retrieves all module versions across all modules.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A collection of all module versions.</returns>
    Task<IEnumerable<ModuleVersionDto>> GetAllModuleVersionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves all versions belonging to a specific module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A collection of module versions for the specified module.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the module with the specified ID does not exist.</exception>
    Task<IEnumerable<ModuleVersionDto>> GetModuleVersionsByModuleIdAsync(Guid moduleId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific version of a module by the module ID and version string.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="version">The version string of the module version.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The requested module version.</returns>
    /// <exception cref="ArgumentException">Thrown if the version string is empty.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if the module or version does not exist.</exception>
    Task<ModuleVersionDto> GetModuleVersionByIdAsync(Guid moduleId, string version, CancellationToken ct = default);

    /// <summary>
    /// Creates and deploys a new version for an existing module from an uploaded ZIP archive.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="dto">The details and binary archive for the new module version.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the module version creation and deployment.</returns>
    /// <exception cref="ArgumentException">Thrown if the provided version details are invalid.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if the module with the specified ID does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the version already exists or the module is not in a valid state for updating.</exception>
    Task<ModuleCreationResultDto> CreateModuleVersionAsync(Guid moduleId, ModuleVersionCreateDto dto, CancellationToken ct = default);

    /// <summary>
    /// Rolls back a module to a previously deployed version, rebuilding and starting its container.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="version">The target version string to roll back to.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated module details after rolling back.</returns>
    /// <exception cref="ArgumentException">Thrown if the version string is empty.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if the module or target version does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the module cannot be rolled back in its current state or is already on the target version.</exception>
    Task<ModuleDetailDto> RollbackModuleVersionAsync(Guid moduleId, string version, CancellationToken ct = default);

    /// <summary>
    /// Updates metadata (version string and/or changelog) of an existing module version.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="version">The current version string of the module version to update.</param>
    /// <param name="dto">The updated version metadata.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown if a version string is empty.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if the module or version does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the updated version name conflicts with another existing version.</exception>
    Task UpdateModuleVersionAsync(Guid moduleId, string version, ModuleVersionUpdateDto dto, CancellationToken ct = default);

    /// <summary>
    /// Deletes an inactive version of a module and removes its stored files.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="version">The version string of the module version to delete.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown if the version string is empty.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if the module or version does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if attempting to delete the currently active version of the module.</exception>
    Task DeleteModuleVersionAsync(Guid moduleId, string version, CancellationToken ct = default);
}