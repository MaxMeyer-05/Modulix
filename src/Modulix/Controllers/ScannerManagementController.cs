using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

using Modulix.Scanner.Interfaces;
using Modulix.Services.Interfaces;

namespace Modulix.Controllers;

/// <summary>
/// Manages the scanners within the endpoint management system.
/// </summary>
[Authorize]
[ApiController]
[Route("api/scanner-management/scanner")]
public class ScannerManagementController : ControllerBase
{
    /// <summary>
    /// The module service used to interact with module data and operations.
    /// </summary>
    private readonly IModuleService _moduleService;

    /// <summary>
    /// The scanner service used to manage module scans.
    /// </summary>
    private readonly IScannerService _scannerService;

    /// <summary>
    /// The module endpoint scanner used by the controller.
    /// </summary>
    private readonly IModuleEndpointScanner _endpointScanner;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScannerManagementController"/> class.
    /// </summary>
    /// <param name="moduleService">The module service used to interact with module data and operations.</param>
    /// <param name="scannerService">The scanner service used to manage module scans.</param>
    /// <param name="endpointScanner">The module endpoint scanner used by the controller.</param>
    public ScannerManagementController(
        IModuleService moduleService, 
        IScannerService scannerService,
        IModuleEndpointScanner endpointScanner)
    {
        _moduleService = moduleService;
        _scannerService = scannerService;
        _endpointScanner = endpointScanner;
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
        var result = await _scannerService.GetScanQueueAsync(ct);
        return Ok(result);
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