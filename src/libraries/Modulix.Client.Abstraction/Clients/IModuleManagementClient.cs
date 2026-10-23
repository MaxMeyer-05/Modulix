using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;

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
    /// <returns>The HTTP response message resulting from the module creation request.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> CreateModuleAsync(CreateModuleDto request, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Stops a specific module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the module stop request.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> StopModuleAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Starts a specific module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the module start request.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> StartModuleAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Retrieves all modules.
    /// </summary>
    /// <param name="query">Optional - The module query parameters.</param>
    /// <param name="pagination">Optional - The pagination query parameters.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the request to retrieve all modules.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> GetAllModulesAsync(ModuleQueryDto? query = null, PaginationQueryDto? pagination = null, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Retrieves the details of a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the request to retrieve the specified module.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> GetModuleByIdAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Retrieves the container logs of a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="tail">Optional - The number of lines from the end of the logs to return.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the request to retrieve the container logs of the specified module.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> GetModuleContainerLogsAsync(Guid moduleId, int? tail = null, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Updates the details of a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="request">The update request containing the new module details.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the request to update the specified module.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> UpdateModuleAsync(Guid moduleId, UpdateModuleDto request, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Deletes a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the request to delete the specified module.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> DeleteModuleAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null);
}