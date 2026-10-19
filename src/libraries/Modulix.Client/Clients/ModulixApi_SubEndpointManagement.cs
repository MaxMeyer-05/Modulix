using Modulix.Client.Clients;
using Modulix.Client.Models.Dtos;

namespace Modulix.Client;

/// <inheritdoc cref="ISubEndpointManagementClient"/>
internal partial class ModulixApi : ISubEndpointManagementClient
{
    /// <summary>
    /// The endpoint for sub-endpoint management within the Modulix API.
    /// </summary>
    private const string SubEndpointManagementEndpoint = "api/endpoint-management/sub-endpoints";

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> ConfirmEndpointAsync(Guid moduleId, ConfirmEndpointsDto request, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{SubEndpointManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/confirm-endpoints";
        return await _httpClient.SendRequestAsync(HttpMethod.Post, requestUri, request, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetAllEndpointsDetailAsync(CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{SubEndpointManagementEndpoint}/endpoints";
        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetModuleEndpointsAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{SubEndpointManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/endpoints";
        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }
}