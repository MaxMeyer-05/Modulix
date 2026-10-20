using System.Globalization;
using System.Net.Http.Headers;

using Modulix.Client.Clients;
using Modulix.Client.Models.Dtos;

namespace Modulix.Client;

/// <inheritdoc cref="IModuleVersionClient"/>
internal partial class ModulixApi : IModuleVersionClient
{
    private const string ModuleVersionEndpoint = "api/version-management/module-versions";

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> CreateModuleVersionAsync(Guid moduleId, ModuleVersionCreateDto dto, CancellationToken ct = default, string? authToken = null)
    {
        using var content = new MultipartFormDataContent();

        content.Add(new StringContent(dto.Version), "version");

        if (dto.Changelog is not null)
            content.Add(new StringContent(dto.Changelog), "changelog");

        content.Add(new StreamContent(dto.ModuleFile.OpenReadStream()),
            nameof(dto.ModuleFile), Path.GetFileName(dto.ModuleFile.FileName));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ModuleVersionEndpoint}/{moduleId}") { Content = content };
        if (!string.IsNullOrWhiteSpace(authToken))
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", authToken);

        return await _httpClient.SendAsync(request, ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> DeleteModuleVersionAsync(Guid moduleId, string version, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleVersionEndpoint}/{moduleId}/version/{version}/delete";
        return await _httpClient.SendRequestAsync(HttpMethod.Delete, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetAllModuleVersionsAsync(CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleVersionEndpoint}";
        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetModuleVersionByIdAsync(Guid moduleVersionId, string version, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleVersionEndpoint}/{moduleVersionId}/version/{version}";
        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetModuleVersionsByModuleIdAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleVersionEndpoint}/{moduleId}";
        return await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> RollbackModuleVersionAsync(Guid moduleId, string version, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleVersionEndpoint}/{moduleId}/version/{version}/rollback";
        return await _httpClient.SendRequestAsync(HttpMethod.Post, requestUri, authToken: authToken, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> UpdateModuleVersionAsync(Guid moduleId, string version, ModuleVersionUpdateDto dto, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleVersionEndpoint}/{moduleId}/version/{version}";
        return await _httpClient.SendRequestAsync(HttpMethod.Put, requestUri, authToken: authToken, ct: ct);
    }
}