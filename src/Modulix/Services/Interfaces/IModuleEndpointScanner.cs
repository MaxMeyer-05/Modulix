using Modulix.Client.Models.Dtos;

using Modulix.Models.Enums;

namespace Modulix.Services.Interfaces;

/// <summary>
/// Provides functionality for discovering runnable entry points and HTTP endpoints 
/// from compiled module assemblies via isolated Docker containers.
/// </summary>
public interface IModuleEndpointScanner
{
    /// <summary>
    /// Scans the specified module storage directory by executing the isolated scanner container.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module to be scanned.</param>
    /// <param name="moduleDirectoryPath">The path to the module's storage directory on the host system.</param>
    /// <param name="priority">The priority of the scan job.</param>
    /// <param name="ct">The cancellation token to cancel the scan operation.</param>
    /// <returns>The task result contains the scan result.</returns>
    Task<ScanEnqueueResponse> ScanOrEnqueueAsync(
        Guid moduleId,
        string moduleDirectoryPath, 
        ScanPriority priority = ScanPriority.Create, 
        CancellationToken ct = default);

    /// <summary>
    /// Cancels the scan job for the specified module, if it is currently queued or running.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module whose scan should be canceled.</param>
    /// <returns><c>true</c> if the scan was successfully canceled; otherwise, <c>false</c>.</returns>
    bool CancelScan(Guid moduleId);
}