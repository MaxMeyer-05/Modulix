using System.Text.Json;
using System.Net;
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
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/cancel-scan";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Delete, requestUri, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task ConfirmEndpointAsync(Guid moduleId, ConfirmEndpointsDto request, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/confirm-endpoints";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Post, requestUri, request, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task<ModuleCreationResultDto> CreateModuleAsync(CreateModuleDto request, CancellationToken ct = default, string? authToken = null)
    {
        var response = await CreateModuleWithStatusAsync(request, ct, authToken);
        return response.Result;
    }

    /// <inheritdoc/>
    public async Task<(HttpStatusCode StatusCode, ModuleCreationResultDto Result)> CreateModuleWithStatusAsync(CreateModuleDto request, CancellationToken ct = default, string? authToken = null)
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
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                string.IsNullOrWhiteSpace(errorContent) ? response.ReasonPhrase : errorContent,
                null, response.StatusCode);
        }
        var result = await response.Content.ReadFromJsonAsync<ModuleCreationResultDto>(
            cancellationToken: ct) ?? throw new JsonException("The API returned an empty response.");
        return (response.StatusCode, result);
    }

    /// <inheritdoc/>
    public async Task DeleteModuleAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/delete";
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
    public async Task<List<ModuleEndpointsDetailDto>> GetAllEndpointsDetailAsync(CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/endpoints";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<ModuleEndpointsDetailDto>>() 
            ?? throw new JsonException("The API returned an empty response.");
    }

    /// <inheritdoc/>
    public async Task<List<ModuleScanQueueItemDto>> GetScanQueueAsync(CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/queue";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<ModuleScanQueueItemDto>>(cancellationToken: ct)
            ?? throw new JsonException("The API returned an empty response.");
    }

    /// <inheritdoc/>
    public async Task<ModuleDetailDto> GetModuleByIdAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ModuleDetailDto>() 
            ?? throw new JsonException("The API returned an empty response.");
    }

    /// <inheritdoc/>
    public async Task<List<ModuleEndpointDto>> GetModuleEndpointsAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/endpoints";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Get, requestUri, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<ModuleEndpointDto>>() 
            ?? throw new JsonException("The API returned an empty response.");
    }

    /// <inheritdoc/>
    public async Task UpdateModuleAsync(Guid moduleId, UpdateModuleDto request, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Put, requestUri, request, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task<ModuleCreationResultDto> UpdateModuleFilesAsync(Guid moduleId, UpdateModuleFilesDto request, CancellationToken ct = default, string? authToken = null)
    {
        using var content = new MultipartFormDataContent();
        content.Add(
            new StreamContent(request.ModuleFile.OpenReadStream()),
            nameof(request.ModuleFile),
            Path.GetFileName(request.ModuleFile.FileName));

        using var message = new HttpRequestMessage(HttpMethod.Put, $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/files")
        {
            Content = content
        };

        if (!string.IsNullOrWhiteSpace(authToken))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        using var response = await _httpClient.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                string.IsNullOrWhiteSpace(errorContent) ? response.ReasonPhrase : errorContent,
                null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<ModuleCreationResultDto>(cancellationToken: ct)
             ?? throw new JsonException("The API returned an empty response.");
    }

    public async Task<ModuleDetailDto> StopModuleAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/stop";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Post, requestUri, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ModuleDetailDto>() 
            ?? throw new JsonException("The API returned an empty response.");
    }

    public async Task<ModuleDetailDto> StartModuleAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null)
    {
        var requestUri = $"{ModuleManagementEndpoint}/{Uri.EscapeDataString(moduleId.ToString())}/start";
        using var response = await _httpClient.SendRequestAsync(HttpMethod.Post, requestUri, authToken: authToken, ct: ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ModuleDetailDto>() 
            ?? throw new JsonException("The API returned an empty response.");
    }
}