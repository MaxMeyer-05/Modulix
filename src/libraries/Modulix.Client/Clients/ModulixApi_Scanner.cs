using Modulix.Client.Clients;

namespace Modulix.Client;

/// <inheritdoc cref="IScannerClient"/>
internal partial class ModulixApi : IScannerClient
{
    /// <summary>
    /// The endpoint for scanner management within the Modulix API.
    /// </summary>
    private const string ScannerEndpoint = "api/v1/scanner";

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> CancelModuleScanAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ScannerEndpoint}/queue/{Uri.EscapeDataString(moduleId.ToString())}";
        return await _httpClient.SendRequestAsync(HttpMethod.Delete, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetScanQueueAsync(CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ScannerEndpoint}/queue";
        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }
}