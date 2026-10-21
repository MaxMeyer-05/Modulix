using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

using Modulix.Scanner.Interfaces;
using Modulix.Services.Interfaces;

namespace Modulix.Controllers;

/// <summary>
/// Controller for managing modular Docker containers, artifacts, and routing configurations.
/// </summary>
[Authorize]
[ApiController]
[Route("api/modules-management/modules")]
public class ModulesManagementController : ControllerBase
{
    private const long MaximumUploadBytes = 50L * 1024 * 1024; // Maximum allowed upload size for module files (50 MB)

    /// <summary>
    /// The module service used by the controller.
    /// </summary>
    private readonly IModuleService _moduleService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModulesManagementController"/> class.
    /// </summary>
    /// <param name="moduleService">The module service.</param>
    public ModulesManagementController(
        IModuleService moduleService)
    {
        _moduleService = moduleService;
    }

    /// <summary>
    /// Creates a new module.
    /// </summary>
    /// <param name="dto">The module creation data transfer object.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// This can be either a 201 Created response if the module was successfully created,
    /// or a 202 Accepted response if the module is pending confirmation.
    /// A 400 Bad Request response if the input data is invalid.
    /// </returns>
    [HttpPost("create")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumUploadBytes)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ModuleCreationResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ModuleCreationResultDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CreateModuleAsync([FromForm] CreateModuleDto dto, CancellationToken ct = default)
    {
        var result = await _moduleService.CreateModuleAsync(dto, ct);
        
        if (result.Module.Status == ModuleStatus.QueuedForScan || result.Module.Status == ModuleStatus.PendingConfirmation)
        {
            return Accepted(result);
        }
        
        return CreatedAtAction(nameof(GetModuleByIdAsync), new { moduleId = result.Module.Id }, result);
    }

    /// <summary>
    /// Starts a module.
    /// </summary>
    /// <param name="moduleId">The ID of the module to start.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with the details of the started module.
    /// A 404 Not Found response if the module could not be found.
    /// A 409 Conflict response if the module cannot be started in its current state.
    /// </returns>
    [HttpPost("{moduleId}/start")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ModuleDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModuleDetailDto>> StartModuleAsync([FromRoute] Guid moduleId, CancellationToken ct = default)
    {
        var result = await _moduleService.StartModuleAsync(moduleId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Stops a module.
    /// </summary>
    /// <param name="moduleId">The ID of the module to stop.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with the details of the stopped module.
    /// A 404 Not Found response if the module could not be found.
    /// A 409 Conflict response if the module is already stopped or not running.
    /// </returns>
    [HttpPost("{moduleId}/stop")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ModuleDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModuleDetailDto>> StopModuleAsync([FromRoute] Guid moduleId, CancellationToken ct = default)
    {
        var result = await _moduleService.StopModuleAsync(moduleId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves all modules.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with a list of all modules.
    /// </returns>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ModuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ModuleDto>>> GetAllModulesAsync(CancellationToken ct = default)
    {
        var result = await _moduleService.GetAllModulesAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the details of a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with the details of the specified module.
    /// A 404 Not Found response if the module could not be found.
    /// </returns>
    [HttpGet("{moduleId}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ModuleDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModuleDetailDto>> GetModuleByIdAsync(Guid moduleId, CancellationToken ct = default)
    {
        var result = await _moduleService.GetModuleByIdAsync(moduleId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Updates the details of a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="dto">The module update data transfer object.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 204 No Content response indicating that the module was successfully updated.
    /// A 404 Not Found response if the module could not be found.
    /// </returns>
    [HttpPut("{moduleId}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateModuleAsync(Guid moduleId, UpdateModuleDto dto, CancellationToken ct = default)
    {
        await _moduleService.UpdateModuleAsync(moduleId, dto, ct);
        return NoContent();
    }

    /// <summary>
    /// Deletes a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 204 No Content response indicating that the module was successfully deleted.
    /// </returns>
    [HttpDelete("{moduleId}/delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteModuleAsync(Guid moduleId, CancellationToken ct = default)
    {
        await _moduleService.DeleteModuleAsync(moduleId, ct);
        return NoContent();
    }
} 