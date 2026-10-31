using Modulix.Client.Clients;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;

namespace Modulix.Client;

/// <inheritdoc cref="ISubEndpointManagementClient"/>
internal partial class ModulixApi : ISubEndpointManagementClient
{
    /// <summary>
    /// The endpoint for sub-endpoints within the Modulix API.
    /// </summary>
    private const string EndpointsBaseUri = "api/v1";

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> ConfirmEndpointAsync(Guid moduleId, ConfirmEndpointsDto request, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{EndpointsBaseUri}/modules/{Uri.EscapeDataString(moduleId.ToString())}/endpoints/confirm";
        return await _httpClient.SendRequestAsync(HttpMethod.Post, requestUri, request, authToken, ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> DeleteSubEndpointAsync(Guid moduleId, Guid subEndpointId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{EndpointsBaseUri}/modules/{Uri.EscapeDataString(moduleId.ToString())}/endpoints/{Uri.EscapeDataString(subEndpointId.ToString())}";
        return await _httpClient.SendRequestAsync(HttpMethod.Delete, requestUri, authToken, ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetAllEndpointsDetailAsync(SubEndpointQueryDto? query = null, PaginationQueryDto? pagination = null, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{EndpointsBaseUri}/endpoints";
        if (pagination != null)
        {
            var queryParams = new List<string>();
            if (query != null)
            {
                if (query.HttpMethod is not null)
                    queryParams.Add($"httpMethod={query.HttpMethod}");
                if (query.Status is not null)
                    queryParams.Add($"status={query.Status}");
            }
            if (pagination != null)
            {
                queryParams.Add($"page={pagination.Page}");
                queryParams.Add($"pageSize={pagination.PageSize}");
            }
            if (queryParams.Count > 0)
                requestUri += "?" + string.Join("&", queryParams);
        }

        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken, ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetModuleEndpointsAsync(Guid moduleId, SubEndpointQueryDto? query = null, PaginationQueryDto? pagination = null, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{EndpointsBaseUri}/modules/{Uri.EscapeDataString(moduleId.ToString())}/endpoints";
        if (pagination != null)
        {
            var queryParams = new List<string>();
            if (query != null)
            {
                if (query.HttpMethod is not null)
                    queryParams.Add($"httpMethod={query.HttpMethod}");
                if (query.Status is not null)
                    queryParams.Add($"status={query.Status}");
            }
            if (pagination != null)
            {
                queryParams.Add($"page={pagination.Page}");
                queryParams.Add($"pageSize={pagination.PageSize}");
            }
            if (queryParams.Count > 0)
                requestUri += "?" + string.Join("&", queryParams);
        }

        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken, ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> UpdateSubEndpointStatusAsync(Guid moduleId, Guid subEndpointId, UpdateSubEndpointStatusDto request, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{EndpointsBaseUri}/modules/{Uri.EscapeDataString(moduleId.ToString())}/endpoints/{Uri.EscapeDataString(subEndpointId.ToString())}/status";
        return await _httpClient.SendRequestAsync(HttpMethod.Patch, requestUri, request, authToken, ct);
    }
}