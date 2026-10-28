using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;

namespace Modulix.Client.Clients;

/// <summary>
/// Interface for managing sub-endpoints via HTTP client.
/// </summary>
public interface ISubEndpointManagementClient
{
    /// <summary>
    /// Confirms the endpoint for a specific module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="request">The confirmation request containing necessary details.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the endpoint confirmation request.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> ConfirmEndpointAsync(Guid moduleId, ConfirmEndpointsDto request, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Retrieves detailed information about all registered sub-endpoints across all modules.
    /// </summary>
    /// <param name="query">Optional - The sub-endpoint query parameters.</param>
    /// <param name="pagination">Optional - The pagination query parameters.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the request to retrieve all sub-endpoints across all modules.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> GetAllEndpointsDetailAsync(SubEndpointQueryDto? query = null, PaginationQueryDto? pagination = null, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Retrieves all endpoints for a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="query">Optional - The sub-endpoint query parameters.</param>
    /// <param name="pagination">Optional - The pagination query parameters.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the request to retrieve all endpoints for the specified module.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> GetModuleEndpointsAsync(Guid moduleId, SubEndpointQueryDto? query = null, PaginationQueryDto? pagination = null, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Updates the status of a specific sub-endpoint for a module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="subEndpointId">The unique identifier of the sub-endpoint.</param>
    /// <param name="request">The updated status payload.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the request to update the sub-endpoint status.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> UpdateSubEndpointStatusAsync(Guid moduleId, Guid subEndpointId, UpdateSubEndpointStatusDto request, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Deletes a specific sub-endpoint for a module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="subEndpointId">The unique identifier of the sub-endpoint.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the request to delete the sub-endpoint.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> DeleteSubEndpointAsync(Guid moduleId, Guid subEndpointId, CancellationToken ct = default, string? authToken = null);
}