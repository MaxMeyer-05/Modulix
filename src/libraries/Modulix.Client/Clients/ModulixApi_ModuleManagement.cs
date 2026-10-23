using System.Globalization;
using System.Net.Http.Headers;

using Modulix.Client.Clients;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;

namespace Modulix.Client;

/// <inheritdoc cref="IModuleManagementClient"/>
internal partial class ModulixApi : IModuleManagementClient
{
    /// <summary>
    /// The endpoint for module management within the Modulix API.
    /// </summary>
    private const string ModuleManagementEndpoint = "api/modules-management/modules";

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> CreateModuleAsync(CreateModuleDto request, CancellationToken ct = default, string? authToken = null)
    {
        using var content = new MultipartFormDataContent();

        content.Add(new StringContent(request.ModuleName), nameof(request.ModuleName));
        content.Add(new StringContent(request.BaseUrlPath), nameof(request.BaseUrlPath));
        content.Add(new StringContent(request.Version), nameof(request.Version));

        if (request.Description is not null)
            content.Add(new StringContent(request.Description), nameof(request.Description));
        if (request.Changelog is not null)
            content.Add(new StringContent(request.Changelog), nameof(request.Changelog));
        if (request.ContainerPort is int port)
            content.Add(new StringContent(port.ToString(CultureInfo.InvariantCulture)), nameof(request.ContainerPort));

        if (request.InitialEndpoints is not null)
        {
            var index = 0;
            foreach (var endpoint in request.InitialEndpoints)
            {
                content.Add(
                    new StringContent(endpoint.HttpMethod.Method),
                    $"{nameof(request.InitialEndpoints)}[{index}].{nameof(CreateModuleSubEndpointDto.HttpMethod)}");
                content.Add(
                    new StringContent(endpoint.EndpointPath),
                    $"{nameof(request.InitialEndpoints)}[{index}].{nameof(CreateModuleSubEndpointDto.EndpointPath)}");
                index++;
            }
        }

        content.Add(new StreamContent(request.ModuleFile.OpenReadStream()),
            nameof(request.ModuleFile), Path.GetFileName(request.ModuleFile.FileName));

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{ModuleManagementEndpoint}/create") { Content = content };
        if (!string.IsNullOrWhiteSpace(authToken))
            message.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", authToken);

        return await _httpClient.SendAsync(message, ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> DeleteModuleAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/delete";
        return await _httpClient.SendRequestAsync(HttpMethod.Delete, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetAllModulesAsync(ModuleQueryDto? query = null, PaginationQueryDto? pagination = null, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}";
        if (pagination != null)
        {
            var queryParams = new List<string>();
            if (query is not null)
            {
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
        
        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetModuleByIdAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}";
        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetModuleContainerLogsAsync(Guid moduleId, int? tail = null, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/logs";
        if (tail.HasValue)
            requestUri += $"?tail={tail.Value.ToString(CultureInfo.InvariantCulture)}";

        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> UpdateModuleAsync(Guid moduleId, UpdateModuleDto request, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}";
        return await _httpClient.SendRequestAsync(HttpMethod.Put, requestUri, request, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> StopModuleAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/stop";
        return await _httpClient.SendRequestAsync(HttpMethod.Post, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> StartModuleAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/start";
        return await _httpClient.SendRequestAsync(HttpMethod.Post, requestUri, authToken: authToken, ct: ct);
    }
}