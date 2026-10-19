using Modulix.Client.Clients;

namespace Modulix.Client;

/// <inheritdoc cref="IScannerManagementClient"/>
internal partial class ModulixApi : IScannerManagementClient
{
    /// <summary>
    /// The endpoint for scanner management within the Modulix API.
    /// </summary>
    private const string ScannerManagementEndpoint = "api/scanner-management/scanner";

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> CancelModuleScanAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ScannerManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/cancel-scan";
        return await _httpClient.SendRequestAsync(HttpMethod.Delete, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetScanQueueAsync(CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ScannerManagementEndpoint}/queue";
        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }
}