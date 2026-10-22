using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using Modulix.Client.Models.Enums;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;

using Modulix.Services.Interfaces;

namespace Modulix.Controllers;

/// <summary>
/// Controller for managing module versions.
/// </summary>
[Authorize]
[ApiController]
[Route("api/version-management/module-versions")]
public class ModuleVersionsManagementController : ControllerBase
{
    private const long MaximumUploadBytes = 50L * 1024 * 1024; // Maximum allowed upload size for module files (50 MB)

    /// <summary>
    /// The module version service used by the controller.
    /// </summary>
    private readonly IModuleVersionService _moduleVersionService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleVersionsManagementController"/> class.
    /// </summary>
    /// <param name="moduleVersionService">The module version service instance.</param>
    public ModuleVersionsManagementController(IModuleVersionService moduleVersionService)
    {
        _moduleVersionService = moduleVersionService;
    }

    /// <summary>
    /// Retrieves all module versions across all modules.
    /// </summary>
    /// <param name="pagination">The pagination query parameters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with a list of all module versions.
    /// </returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResultDto<ModuleVersionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<ModuleVersionDto>>> GetAllModuleVersionsAsync([FromQuery] PaginationQueryDto pagination, CancellationToken ct = default)
    {
        var result = await _moduleVersionService.GetAllModuleVersionsAsync(pagination, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves all versions for a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with a list of versions for the specified module.
    /// A 404 Not Found response if the module could not be found.
    /// </returns>
    [HttpGet("{moduleId}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(IEnumerable<ModuleVersionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ModuleVersionDto>>> GetModuleVersionsByModuleIdAsync([FromRoute] Guid moduleId, CancellationToken ct = default)
    {
        var result = await _moduleVersionService.GetModuleVersionsByModuleIdAsync(moduleId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves a specific version of a module by the module ID and version string.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="version">The version string.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with the details of the specified module version.
    /// A 400 Bad Request response if the version parameter is invalid.
    /// A 404 Not Found response if the module or version could not be found.
    /// </returns>
    [HttpGet("{moduleId}/version/{version}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ModuleVersionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModuleVersionDto>> GetModuleVersionByIdAsync([FromRoute] Guid moduleId, [FromRoute] string version, CancellationToken ct = default)
    {
        var result = await _moduleVersionService.GetModuleVersionByIdAsync(moduleId, version, ct);
        return Ok(result);
    }

    /// <summary>
    /// Creates and deploys a new version for a specific module.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="dto">The module version creation data transfer object.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 201 Created response if the module version was created and activated,
    /// or a 202 Accepted response if the version scan was queued or is pending endpoint confirmation.
    /// A 400 Bad Request response if the input data is invalid.
    /// A 404 Not Found response if the module could not be found.
    /// A 409 Conflict response if the version already exists or the module cannot be updated.
    /// </returns>
    [HttpPost("{moduleId}/version")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumUploadBytes)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ModuleCreationResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ModuleCreationResultDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CreateModuleVersionAsync([FromRoute] Guid moduleId, [FromForm] ModuleVersionCreateDto dto, CancellationToken ct = default)
    {
        var result = await _moduleVersionService.CreateModuleVersionAsync(moduleId, dto, ct);

        if (result.Module.Status == ModuleStatus.QueuedForScan || result.Module.Status == ModuleStatus.PendingConfirmation)
        {
            return Accepted(result);
        }

        return CreatedAtAction(
            nameof(GetModuleVersionByIdAsync),
            new { moduleId, version = dto.Version.Trim() },
            result);
    }

    /// <summary>
    /// Rolls back a module to a specified earlier version.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="version">The target version string to roll back to.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with the updated module details after rolling back.
    /// A 400 Bad Request response if the version parameter is invalid.
    /// A 404 Not Found response if the module or version could not be found.
    /// A 409 Conflict response if the module cannot be rolled back in its current state.
    /// </returns>
    [HttpPost("{moduleId}/rollback/{version}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ModuleDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModuleDetailDto>> RollbackModuleVersionAsync([FromRoute] Guid moduleId, [FromRoute] string version, CancellationToken ct = default)
    {
        var result = await _moduleVersionService.RollbackModuleVersionAsync(moduleId, version, ct);
        return Ok(result);
    }

    /// <summary>
    /// Updates the metadata of a specific module version.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="version">The version string of the module version to update.</param>
    /// <param name="dto">The module version update data transfer object.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 204 No Content response indicating that the module version was successfully updated.
    /// A 400 Bad Request response if the input data is invalid.
    /// A 404 Not Found response if the module or version could not be found.
    /// A 409 Conflict response if the new version name already exists.
    /// </returns>
    [HttpPut("{moduleId}/version/{version}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateModuleVersionAsync([FromRoute] Guid moduleId, [FromRoute] string version, [FromBody] ModuleVersionUpdateDto dto, CancellationToken ct = default)
    {
        await _moduleVersionService.UpdateModuleVersionAsync(moduleId, version, dto, ct);
        return NoContent();
    }

    /// <summary>
    /// Deletes a specific inactive version of a module.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="version">The version string of the module version to delete.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 204 No Content response indicating that the module version was successfully deleted.
    /// A 400 Bad Request response if the version parameter is invalid.
    /// A 404 Not Found response if the module or version could not be found.
    /// A 409 Conflict response if attempting to delete the currently active version.
    /// </returns>
    [HttpDelete("{moduleId}/version/{version}/delete")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteModuleVersionAsync([FromRoute] Guid moduleId, [FromRoute] string version, CancellationToken ct = default)
    {
        await _moduleVersionService.DeleteModuleVersionAsync(moduleId, version, ct);
        return NoContent();
    }
}