using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;

namespace Modulix.Services.Interfaces;

/// <summary>
/// Defines the interface for sub-endpoint management services within the Modulix application.
/// </summary>
public interface ISubEndpointService
{
    /// <summary>
    /// Confirms the final list of endpoints for a module in PendingConfirmation state.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="dto">The confirmed endpoints for the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated details of the module.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the module is not in PendingConfirmation state.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if the module with the specified ID does not exist.</exception>
    Task<ModuleDetailDto> ConfirmSubEndpointsAsync(Guid moduleId, ConfirmEndpointsDto dto, CancellationToken ct = default);

    /// <summary>
    /// Retrieves detailed information about all registered sub-endpoints across all modules.
    /// </summary>
    /// <param name="query">The sub-endpoint query parameters.</param>
    /// <param name="pagination">The pagination query parameters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A paged result of detailed sub-endpoint information.</returns>
    Task<PagedResultDto<ModuleSubEndpointsDetailDto>> GetAllSubEndpointsDetailAsync(SubEndpointQueryDto? query = null, PaginationQueryDto? pagination = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all registered sub-endpoints for a specific module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="query">The sub-endpoint query parameters.</param>
    /// <param name="pagination">The pagination query parameters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A paged result of all registered sub-endpoints for the specified module.</returns> 
    /// <exception cref="KeyNotFoundException">Thrown if the module with the specified ID does not exist.</exception>
    Task<PagedResultDto<ModuleSubEndpointDto>> GetModuleSubEndpointsAsync(Guid moduleId, SubEndpointQueryDto? query = null, PaginationQueryDto? pagination = null, CancellationToken ct = default);

    /// <summary>
    /// Updates the status of a specific sub-endpoint for a module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="subEndpointId">The unique identifier of the sub-endpoint.</param>
    /// <param name="dto">The status update information for the sub-endpoint.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the module or sub-endpoint with the specified ID does not exist.</exception>
    Task UpdateSubEndpointStatusAsync(Guid moduleId, Guid subEndpointId, UpdateSubEndpointStatusDto dto, CancellationToken ct = default);

    /// <summary>
    /// Deletes a specific sub-endpoint for a module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="subEndpointId">The unique identifier of the sub-endpoint.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the module or sub-endpoint with the specified ID does not exist.</exception>
    Task DeleteSubEndpointAsync(Guid moduleId, Guid subEndpointId, CancellationToken ct = default);
}