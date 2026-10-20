namespace Modulix.Client.Clients;

/// <summary>
/// Interface for managing the scanner via HTTP client.
/// </summary>
public interface IScannerManagementClient
{
    /// <summary>
    /// Retrieves all modules currently in the scan queue along with their scan priority.
    /// </summary>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the request to retrieve the scan queue.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> GetScanQueueAsync(CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Cancels an ongoing scan for a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the request to cancel the scan for the specified module.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> CancelModuleScanAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null);
}