using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;

using Modulix.Services.Interfaces;

namespace Modulix.Controllers;

/// <summary>
/// Manages the sub-endpoints within the endpoint management system.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1")]
public class SubEndpointsManagementController : ControllerBase
{
    /// <summary>
    /// The sub-endpoint service used by the controller.
    /// </summary>
    private readonly ISubEndpointService _subEndpointService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubEndpointsManagementController"/> class.
    /// </summary>
    /// <param name="subEndpointService">The sub-endpoint service instance.</param>
    public SubEndpointsManagementController(ISubEndpointService subEndpointService)
    {
        _subEndpointService = subEndpointService;
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
    [HttpPost("modules/{moduleId}/endpoints/confirm")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ModuleDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModuleDetailDto>> ConfirmModuleEndpointsAsync([FromRoute] Guid moduleId, [FromBody] ConfirmEndpointsDto dto, CancellationToken ct = default)
    { 
        var result = await _subEndpointService.ConfirmSubEndpointsAsync(moduleId, dto, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the sub-endpoints of a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="query">The sub-endpoint query parameters.</param>
    /// <param name="pagination">The pagination query parameters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with a list of sub-endpoints for the specified module.
    /// A 404 Not Found response if the module could not be found.
    /// </returns>
    [HttpGet("modules/{moduleId}/endpoints")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(PagedResultDto<ModuleSubEndpointDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<ModuleSubEndpointDto>>> GetModuleEndpointsAsync([FromRoute] Guid moduleId, [FromQuery] SubEndpointQueryDto query, [FromQuery] PaginationQueryDto pagination, CancellationToken ct = default)
    {
        var result = await _subEndpointService.GetModuleSubEndpointsAsync(moduleId, query, pagination, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves detailed information about all registered sub-endpoints across all modules.
    /// </summary>
    /// <param name="query">The sub-endpoint query parameters.</param>
    /// <param name="pagination">The pagination query parameters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with a list of detailed sub-endpoints.
    /// </returns>
    [HttpGet("endpoints")]
    [ProducesResponseType(typeof(PagedResultDto<ModuleSubEndpointsDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResultDto<ModuleSubEndpointsDetailDto>>> GetAllEndpointsDetailAsync([FromQuery] SubEndpointQueryDto query, [FromQuery] PaginationQueryDto pagination, CancellationToken ct = default)
    {
        var result = await _subEndpointService.GetAllSubEndpointsDetailAsync(query, pagination, ct);
        return Ok(result);
    }

    /// <summary>
    /// Updates the status of a specific sub-endpoint for a module.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="subEndpointId">The ID of the sub-endpoint.</param>
    /// <param name="dto">The updated status payload.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 204 No Content response if the update is successful.
    /// A 400 Bad Request response if the requested status is invalid.
    /// A 404 Not Found response if the module or sub-endpoint could not be found.
    /// A 409 Conflict response if the sub-endpoint cannot be activated in the module's current state.
    /// </returns>
    [HttpPatch("modules/{moduleId}/endpoints/{subEndpointId}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateSubEndpointStatusAsync([FromRoute] Guid moduleId, [FromRoute] Guid subEndpointId, [FromBody] UpdateSubEndpointStatusDto dto, CancellationToken ct = default)
    {
        await _subEndpointService.UpdateSubEndpointStatusAsync(moduleId, subEndpointId, dto, ct);
        return NoContent();
    }

    /// <summary>
    /// Deletes a specific sub-endpoint for a module.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="subEndpointId">The ID of the sub-endpoint.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 204 No Content response if the deletion is successful.
    /// A 404 Not Found response if the module or sub-endpoint could not be found.
    /// </returns>
    [HttpDelete("modules/{moduleId}/endpoints/{subEndpointId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSubEndpointAsync([FromRoute] Guid moduleId, [FromRoute] Guid subEndpointId, CancellationToken ct = default)
    {
        await _subEndpointService.DeleteSubEndpointAsync(moduleId, subEndpointId, ct);
        return NoContent();
    }
}