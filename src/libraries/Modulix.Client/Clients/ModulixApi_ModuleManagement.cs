using System.Text.Json;
using System.Net.Http.Json;
using System.Globalization;

using Modulix.Client.Clients;
using Modulix.Client.Models.Dtos;
using System.Net.Http.Headers;

namespace Modulix.Client;

internal partial class ModulixApi : IModuleManagementClient
{
    /// <summary>
    /// The endpoint for module management within the Modulix API.
    /// </summary>
    private const string ModuleManagementEndpoint = "api/modules-management/modules";

    /// <inheritdoc/>
    public async Task CancelModuleScanAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{moduleId}/cancel-scan";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Post, requestUri, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task ConfirmEndpointAsync(Guid moduleId, ConfirmEndpointsDto request, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{moduleId}/confirm-endpoint";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Post, requestUri, request, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task<ModuleCreationResultDto> CreateModuleAsync(CreateModuleDto request, CancellationToken ct = default, string? authToken = null)
    {
        using var content = new MultipartFormDataContent();

        content.Add(new StringContent(request.ModuleName), nameof(request.ModuleName));
        content.Add(new StringContent(request.BaseUrlPath), nameof(request.BaseUrlPath));

        if (request.Description is not null)
            content.Add(new StringContent(request.Description), nameof(request.Description));
        if (request.ContainerPort is int port)
            content.Add(new StringContent(port.ToString(CultureInfo.InvariantCulture)), nameof(request.ContainerPort));

        content.Add(new StreamContent(request.ModuleFile.OpenReadStream()),
            nameof(request.ModuleFile), Path.GetFileName(request.ModuleFile.FileName));

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{ModuleManagementEndpoint}/create") { Content = content };
        if (!string.IsNullOrWhiteSpace(authToken))
            message.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", authToken);

        using var response = await _httpClient.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ModuleCreationResultDto>(
            cancellationToken: ct) ?? throw new JsonException("The API returned an empty response.");
    }

    /// <inheritdoc/>
    public async Task DeleteModuleAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{moduleId}/delete";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Delete, requestUri, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task<List<ModuleDto>> GetAllModulesAsync(CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<ModuleDto>>() 
            ?? throw new JsonException("The API returned an empty response.");
    }

    /// <inheritdoc/>
    public async Task<ModuleDetailDto> GetModuleByIdAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{moduleId}";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ModuleDetailDto>() 
            ?? throw new JsonException("The API returned an empty response.");
    }

    /// <inheritdoc/>
    public async Task<List<ModuleEndpointDto>> GetModuleEndpointsAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{moduleId}/endpoints";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<ModuleEndpointDto>>() 
            ?? throw new JsonException("The API returned an empty response.");
    }

    /// <inheritdoc/>
    public async Task UpdateModuleAsync(Guid moduleId, UpdateModuleDto request, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{moduleId}";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Put, requestUri, request, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task<ModuleCreationResultDto> UpdateModuleFilesAsync(Guid moduleId, UpdateModuleFilesDto request, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{moduleId}/files";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Put, requestUri, request, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ModuleCreationResultDto>() 
            ?? throw new JsonException("The API returned an empty response.");
    }
}