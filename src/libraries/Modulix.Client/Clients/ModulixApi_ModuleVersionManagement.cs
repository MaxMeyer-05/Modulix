using System.Net.Http.Headers;

using Modulix.Client.Clients;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;

namespace Modulix.Client;

/// <inheritdoc cref="IModuleVersionClient"/>
internal partial class ModulixApi : IModuleVersionClient
{
    private const string ModuleVersionEndpoint = "api/version-management/module-versions";

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> CreateModuleVersionAsync(Guid moduleId, ModuleVersionCreateDto dto, CancellationToken ct = default, string? authToken = null)
    {
        using var content = new MultipartFormDataContent();

        content.Add(new StringContent(dto.Version), nameof(dto.Version));

        if (dto.Changelog is not null)
            content.Add(new StringContent(dto.Changelog), nameof(dto.Changelog));

        content.Add(new StreamContent(dto.ModuleFile.OpenReadStream()),
            nameof(dto.ModuleFile), Path.GetFileName(dto.ModuleFile.FileName));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ModuleVersionEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/version") { Content = content };
        if (!string.IsNullOrWhiteSpace(authToken))
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", authToken);

        return await _httpClient.SendAsync(request, ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> DeleteModuleVersionAsync(Guid moduleId, string version, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleVersionEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/version/{Uri.EscapeDataString(version)}/delete";
        return await _httpClient.SendRequestAsync(HttpMethod.Delete, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetAllModuleVersionsAsync(ModuleVersionQueryDto? query = null, PaginationQueryDto? pagination = null, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleVersionEndpoint}";
        var queryParams = new List<string>();
        if (query is not null)
        {
            if (query.Version is not null)
                queryParams.Add($"version={query.Version}");
        }

        if (pagination != null)
        {
            queryParams.Add($"page={pagination.Page}");
            queryParams.Add($"pageSize={pagination.PageSize}");
        }

        if (queryParams.Count > 0)
            requestUri += "?" + string.Join("&", queryParams);

        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetModuleVersionByIdAsync(Guid moduleVersionId, string version, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleVersionEndpoint}/{Uri.EscapeDataString(moduleVersionId.ToString())}/version/{Uri.EscapeDataString(version)}";
        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetModuleVersionsByModuleIdAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleVersionEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}";
        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> RollbackModuleVersionAsync(Guid moduleId, string version, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleVersionEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/rollback/{Uri.EscapeDataString(version)}";
        return await _httpClient.SendRequestAsync(HttpMethod.Post, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> UpdateModuleVersionAsync(Guid moduleId, string version, ModuleVersionUpdateDto dto, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleVersionEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/version/{Uri.EscapeDataString(version)}";
        return await _httpClient.SendRequestAsync(HttpMethod.Put, requestUri, dto, authToken: authToken, ct: ct);
    }
}