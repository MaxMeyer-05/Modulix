using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using Modulix.Models.Dtos;
using Modulix.Models.Enums;

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
    /// The module endpoint scanner used by the controller.
    /// </summary>
    private readonly IModuleEndpointScanner _endpointScanner;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModulesManagementController"/> class.
    /// </summary>
    /// <param name="moduleService">The module service.</param>
    /// <param name="endpointScanner">The module endpoint scanner.</param>
    public ModulesManagementController(
        IModuleService moduleService, 
        IModuleEndpointScanner endpointScanner)
    {
        _moduleService = moduleService;
        _endpointScanner = endpointScanner;
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
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumUploadBytes)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ModuleCreationResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ModuleCreationResultDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CreateModuleAsync([FromForm] CreateModuleDto dto)
    {
        var result = await _moduleService.CreateModuleAsync(dto, HttpContext.RequestAborted);
        
        if (result.Module.Status == ModuleStatus.QueuedForScan || result.Module.Status == ModuleStatus.PendingConfirmation)
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
    [HttpGet("{moduleId}/endpoints")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(IEnumerable<ModuleEndpointDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ModuleEndpointDto>>> GetModuleEndpointsAsync(Guid moduleId)
    {
        var result = await _moduleService.GetModuleEndpointsAsync(moduleId, HttpContext.RequestAborted);
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
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumUploadBytes)]
    [ProducesResponseType(typeof(ModuleCreationResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModuleCreationResultDto>> UpdateModuleFilesAsync(Guid moduleId, [FromForm] UpdateModuleFilesDto dto)
    {
        var result = await _moduleService.UpdateModuleFilesAsync(moduleId, dto, HttpContext.RequestAborted);
        return Ok(result);
    }

    /// <summary>
    /// Deletes a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <returns>
    /// A 204 No Content response indicating that the module was successfully deleted.
    /// </returns>
    [HttpDelete("{moduleId}/delete")]
    [Authorize(Roles = nameof(Roles.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteModuleAsync(Guid moduleId)
    {
        await _moduleService.DeleteModuleAsync(moduleId, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>
    /// Cancels the scan of a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <returns>
    /// A 204 No Content response indicating that the scan was successfully cancelled.
    /// A 400 Bad Request response if the module is not queued for scanning.
    /// A 409 Conflict response if the scan has already started or completed.
    /// </returns>
    [HttpDelete("{moduleId}/cancel-scan")]
    [Authorize(Roles = nameof(Roles.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelScanAsync(Guid moduleId)
    {
        var module = await _moduleService.GetModuleByIdAsync(moduleId, HttpContext.RequestAborted);
        if (module.Status != ModuleStatus.QueuedForScan)
            return BadRequest(new ProblemDetails { Detail = "Module is not queued for scanning." });

        var cancelled = _endpointScanner.CancelScan(moduleId);
        if (cancelled)
        {
            await _moduleService.DeleteModuleAsync(moduleId, HttpContext.RequestAborted);
            return NoContent();
        }

        return Conflict(new ProblemDetails { Detail = "Scan has already started or completed." });
    }
} 