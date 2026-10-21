using Modulix.Client.Models.Dtos;

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
    /// <param name="pagination">The pagination query parameters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A paged result of detailed sub-endpoint information.</returns>
    Task<PagedResultDto<ModuleSubEndpointsDetailDto>> GetAllSubEndpointsDetailAsync(PaginationQueryDto pagination, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all registered sub-endpoints for a specific module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="pagination">The pagination query parameters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A paged result of all registered sub-endpoints for the specified module.</returns> 
    /// <exception cref="KeyNotFoundException">Thrown if the module with the specified ID does not exist.</exception>
    Task<PagedResultDto<ModuleSubEndpointDto>> GetModuleSubEndpointsAsync(Guid moduleId, PaginationQueryDto pagination, CancellationToken ct = default);
}