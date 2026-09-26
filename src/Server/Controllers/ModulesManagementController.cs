using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using Server.Models.Dtos;
using Server.Models.Enums;

using Server.Services.Interfaces;

namespace Server.Controllers;

/// <summary>
/// Controller for managing modular Docker containers, artifacts, and routing configurations.
/// </summary>
[Authorize]
[ApiController]
[Route("api/modules-management/modules")]
public class ModulesManagementController : ControllerBase
{
    /// <summary>
    /// The module service used by the controller.
    /// </summary>
    private readonly IModuleService _moduleService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModulesManagementController"/> class.
    /// </summary>
    /// <param name="moduleService">The module service.</param>
    public ModulesManagementController(IModuleService moduleService)
    {
        _moduleService = moduleService;
    }

    /// <summary>
    /// Creates a new module.
    /// </summary>
    /// <param name="dto">The module creation data transfer object.</param>
    /// <returns>
    /// This can be either a 201 Created response if the module was successfully created,
    /// or a 202 Accepted response if the module is pending confirmation.
    /// A 400 Bad Request response if the input data is invalid.
    /// </returns>
    [HttpPost("create")]
    [Authorize(Roles = nameof(Roles.Admin))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ModuleCreationResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ModuleCreationResultDto), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ModuleCreationResultDto>> CreateModuleAsync(CreateModuleDto dto)
    {
        if (dto == null)
            return BadRequest();
            
        var result = await _moduleService.CreateModuleAsync(dto, HttpContext.RequestAborted);

        if (result.Module.Status == ModuleStatus.PendingConfirmation)
        {
            return Accepted(result);
        }

        return CreatedAtAction(nameof(GetModuleByIdAsync), new { moduleId = result.Module.Id }, result);
    }

    /// <summary>
    /// Confirms the endpoints of a module.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="dto">The confirmation data transfer object.</param>
    /// <returns>
    /// A 200 OK response with the details of the confirmed module.
    /// A 400 Bad Request response if the input data is invalid.
    /// A 404 Not Found response if the module could not be found.
    /// A 409 Conflict response if the module is already confirmed.
    /// </returns>
    [Authorize(Roles = nameof(Roles.Admin))]
    [HttpPost("{moduleId}/confirm-endpoints")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ModuleDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModuleDetailDto>> ConfirmModuleEndpointsAsync([FromRoute] Guid moduleId, [FromBody] ConfirmEndpointsDto dto)
    {   
        if (dto == null)
            return BadRequest();

        var result = await _moduleService.ConfirmEndpointsAsync(moduleId, dto, HttpContext.RequestAborted);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves all modules.
    /// </summary>
    /// <returns>
    /// A 200 OK response with a list of all modules.
    /// </returns>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ModuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ModuleDto>>> GetAllModulesAsync()
    {
        var result = await _moduleService.GetAllModulesAsync(HttpContext.RequestAborted);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the details of a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <returns>
    /// A 200 OK response with the details of the specified module.
    /// A 404 Not Found response if the module could not be found.
    /// </returns>
    [HttpGet("{moduleId}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ModuleDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModuleDetailDto>> GetModuleByIdAsync(Guid moduleId)
    {
        var result = await _moduleService.GetModuleByIdAsync(moduleId, HttpContext.RequestAborted);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the sub-endpoints of a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <returns>
    /// A 200 OK response with a list of sub-endpoints for the specified module.
    /// A 404 Not Found response if the module could not be found.
    /// </returns>
    [HttpGet("{moduleId}/sub-endpoints")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(IEnumerable<ModuleEndpointDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ModuleEndpointDto>>> GetModuleSubEndpointsAsync(Guid moduleId)
    {
        var result = await _moduleService.GetModuleSubEndpointsAsync(moduleId, HttpContext.RequestAborted);
        return Ok(result);
    }

    /// <summary>
    /// Updates the details of a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="dto">The module update data transfer object.</param>
    /// <returns>
    /// A 204 No Content response indicating that the module was successfully updated.
    /// A 404 Not Found response if the module could not be found.
    /// </returns>
    [HttpPut("{moduleId}")]
    [Authorize(Roles = nameof(Roles.Admin))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateModuleAsync(Guid moduleId, UpdateModuleDto dto)
    {
        await _moduleService.UpdateModuleAsync(moduleId, dto, HttpContext.RequestAborted);
        return NoContent();
    }
    /// <summary>
    /// Updates the files of a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="dto">The module files update data transfer object.</param>
    /// <returns>
    /// A 204 No Content response indicating that the module files were successfully updated.
    /// A 404 Not Found response if the module could not be found.
    /// A 409 Conflict response if the module is already confirmed.
    /// </returns>
    [HttpPut("{moduleId}/files")]
    [Authorize(Roles = nameof(Roles.Admin))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateModuleFilesAsync(Guid moduleId, UpdateModuleFilesDto dto)
    {
        await _moduleService.UpdateModuleFilesAsync(moduleId, dto, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>
    /// Deletes a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <returns>
    /// A 204 No Content response indicating that the module was successfully deleted.
    /// </returns>
    [HttpDelete("{moduleId}")]
    [Authorize(Roles = nameof(Roles.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteModuleAsync(Guid moduleId)
    {
        await _moduleService.DeleteModuleAsync(moduleId, HttpContext.RequestAborted);
        return NoContent();
    }
} 