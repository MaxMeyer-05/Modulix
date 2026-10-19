using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

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
    /// Confirms the endpoints of a module.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="dto">The confirmation data transfer object.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with the details of the confirmed module.
    /// A 400 Bad Request response if the input data is invalid.
    /// A 404 Not Found response if the module could not be found.
    /// A 409 Conflict response if the module is already confirmed.
    /// </returns>
    [HttpPost("{moduleId}/confirm-endpoints")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ModuleDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModuleDetailDto>> ConfirmModuleEndpointsAsync([FromRoute] Guid moduleId, [FromBody] ConfirmEndpointsDto dto, CancellationToken ct = default)
    { 
        var result = await _moduleService.ConfirmEndpointsAsync(moduleId, dto, ct);
        return Ok(result);
    }

    /// <summary>
    /// Starts a module.
    /// </summary>
    /// <param name="moduleId">The ID of the module to start.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with the details of the started module.
    /// A 404 Not Found response if the module could not be found.
    /// </returns>
    [HttpPost("{moduleId}/start")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
    /// </returns>
    [HttpPost("{moduleId}/stop")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
    /// Retrieves detailed information about all registered sub-endpoints across all modules.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with a list of detailed sub-endpoints.
    /// </returns>
    [HttpGet("endpoints")]
    [ProducesResponseType(typeof(IEnumerable<ModuleEndpointsDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ModuleEndpointsDetailDto>>> GetAllEndpointsDetailAsync(CancellationToken ct = default)
    {
        var result = await _moduleService.GetAllEndpointsDetailAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves all modules currently in the scan queue along with their scan priority.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with a list of queued module scans.
    /// </returns>
    [HttpGet("queue")]
    [ProducesResponseType(typeof(IEnumerable<ModuleScanQueueItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ModuleScanQueueItemDto>>> GetScanQueueAsync(CancellationToken ct = default)
    {
        var result = await _moduleService.GetScanQueueAsync(ct);
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
    /// Retrieves the sub-endpoints of a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with a list of sub-endpoints for the specified module.
    /// A 404 Not Found response if the module could not be found.
    /// </returns>
    [HttpGet("{moduleId}/endpoints")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(IEnumerable<ModuleEndpointDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ModuleEndpointDto>>> GetModuleEndpointsAsync(Guid moduleId, CancellationToken ct = default)
    {
        var result = await _moduleService.GetModuleEndpointsAsync(moduleId, ct);
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
    /// Updates the files of a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="dto">The module files update data transfer object.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 204 No Content response indicating that the module files were successfully updated.
    /// A 404 Not Found response if the module could not be found.
    /// A 409 Conflict response if the module is already confirmed.
    /// </returns>
    [HttpPut("{moduleId}/files")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumUploadBytes)]
    [ProducesResponseType(typeof(ModuleCreationResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModuleCreationResultDto>> UpdateModuleFilesAsync(Guid moduleId, [FromForm] UpdateModuleFilesDto dto, CancellationToken ct = default)
    {
        var result = await _moduleService.UpdateModuleFilesAsync(moduleId, dto, ct);
        return Ok(result);
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

    /// <summary>
    /// Cancels the scan of a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 204 No Content response indicating that the scan was successfully cancelled.
    /// A 400 Bad Request response if the module is not queued for scanning.
    /// A 409 Conflict response if the scan has already started or completed.
    /// </returns>
    [HttpDelete("{moduleId}/cancel-scan")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelScanAsync(Guid moduleId, CancellationToken ct = default)
    {
        var module = await _moduleService.GetModuleByIdAsync(moduleId, ct);
        if (module.Status != ModuleStatus.QueuedForScan)
            return BadRequest(new ProblemDetails { Detail = "Module is not queued for scanning." });

        var cancelled = _endpointScanner.CancelScan(moduleId);
        if (cancelled)
        {
            await _moduleService.DeleteModuleAsync(moduleId, ct);
            return NoContent();
        }

        return Conflict(new ProblemDetails { Detail = "Scan has already started or completed." });
    }
} 