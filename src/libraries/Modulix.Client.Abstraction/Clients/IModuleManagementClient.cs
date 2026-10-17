using System.Net;

using Modulix.Client.Models.Dtos;

namespace Modulix.Client.Clients;

/// <summary>
/// Interface for managing modules via HTTP client.
/// </summary>
public interface IModuleManagementClient
{
    /// <summary>
    /// Creates a new module.
    /// </summary>
    /// <param name="request">The module creation request containing necessary details.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The result of the module creation operation.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<ModuleCreationResultDto> CreateModuleAsync(CreateModuleDto request, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Creates a module and returns the API's HTTP status code with the result.
    /// </summary>
    /// <param name="request">The module creation request containing necessary details.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP status code and the result of the module creation operation.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<(HttpStatusCode StatusCode, ModuleCreationResultDto Result)> CreateModuleWithStatusAsync(CreateModuleDto request, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Confirms the endpoint for a specific module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="request">The confirmation request containing necessary details.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task ConfirmEndpointAsync(Guid moduleId, ConfirmEndpointsDto request, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Retrieves all modules.
    /// </summary>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>A list of all modules.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<List<ModuleDto>> GetAllModulesAsync(CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Retrieves detailed information about all registered sub-endpoints across all modules.
    /// </summary>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>A list of detailed sub-endpoints across all modules.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<List<ModuleEndpointsDetailDto>> GetAllEndpointsDetailAsync(CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Retrieves the details of a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The detailed information of the specified module.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<ModuleDetailDto> GetModuleByIdAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Retrieves all endpoints for a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>A list of all endpoints for the specified module.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<List<ModuleEndpointDto>> GetModuleEndpointsAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Updates the details of a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="request">The update request containing the new module details.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task UpdateModuleAsync(Guid moduleId, UpdateModuleDto request, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Updates the files of a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="request">The update request containing the new module files.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<ModuleCreationResultDto> UpdateModuleFilesAsync(Guid moduleId, UpdateModuleFilesDto request, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Deletes a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task DeleteModuleAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Cancels an ongoing scan for a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task CancelModuleScanAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null);
}