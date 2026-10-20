using Modulix.Client.Models.Dtos;

namespace Modulix.Services.Interfaces;

/// <summary>
/// Defines the interface for scanner management services within the Modulix application.
/// </summary>
public interface IScannerService
{
    /// <summary>
    /// Retrieves all modules currently in the scan queue along with their scan priority.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A collection of queued module scan entries.</returns>
    Task<IEnumerable<ModuleScanQueueItemDto>> GetScanQueueAsync(CancellationToken ct = default);

    /// <summary>
    /// Processes the result of a previously queued module scan.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module whose scan result is being processed.</param>
    /// <param name="result">The result of the module scan.</param>
    /// <param name="ct">The cancellation token.</param>
    Task ProcessQueuedScanResultAsync(Guid moduleId, ModuleScanResultDto result, CancellationToken ct = default);
}