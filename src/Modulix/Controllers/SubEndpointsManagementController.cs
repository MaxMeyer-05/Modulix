using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using Modulix.Client.Models.Dtos;

using Modulix.Services.Interfaces;

namespace Modulix.Controllers;

/// <summary>
/// Manages the sub-endpoints within the endpoint management system.
/// </summary>
[Authorize]
[ApiController]
[Route("api/endpoint-management/sub-endpoints")]
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
    [HttpPost("{moduleId}/confirm")]
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
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with a list of sub-endpoints for the specified module.
    /// A 404 Not Found response if the module could not be found.
    /// </returns>
    [HttpGet("{moduleId}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(IEnumerable<ModuleSubEndpointDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ModuleSubEndpointDto>>> GetModuleEndpointsAsync(Guid moduleId, CancellationToken ct = default)
    {
        var result = await _subEndpointService.GetModuleSubEndpointsAsync(moduleId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves detailed information about all registered sub-endpoints across all modules.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A 200 OK response with a list of detailed sub-endpoints.
    /// </returns>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ModuleSubEndpointsDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ModuleSubEndpointsDetailDto>>> GetAllEndpointsDetailAsync(CancellationToken ct = default)
    {
        var result = await _subEndpointService.GetAllSubEndpointsDetailAsync(ct);
        return Ok(result);
    }
}