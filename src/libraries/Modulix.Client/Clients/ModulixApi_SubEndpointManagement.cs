using Modulix.Client.Clients;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;

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
        var requestUri = $"{SubEndpointManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/confirm";
        return await _httpClient.SendRequestAsync(HttpMethod.Post, requestUri, request, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetAllEndpointsDetailAsync(PaginationQueryDto? pagination = null, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{SubEndpointManagementEndpoint}";
        if (pagination != null)
        {
            var query = new List<string>
            {
                $"page={pagination.Page}",
                $"pageSize={pagination.PageSize}"
            };
            if (query.Count > 0)
                requestUri += "?" + string.Join("&", query);
        }

        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetModuleEndpointsAsync(Guid moduleId, PaginationQueryDto? pagination = null, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{SubEndpointManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}";
        if (pagination != null)
        {
            var query = new List<string>
            {
                $"page={pagination.Page}",
                $"pageSize={pagination.PageSize}"
            };
            if (query.Count > 0)
                requestUri += "?" + string.Join("&", query);
        }

        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }
}