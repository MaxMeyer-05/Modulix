using Modulix.Client.Models.Dtos;

namespace Modulix.Client.Clients;

/// <summary>
/// Defines the contract for interacting with module versions in the Modulix system.
/// </summary>
public interface IModuleVersionClient
{
    /// <summary>
    /// Retrieves all module versions.
    /// </summary>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the module creation request.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> GetAllModuleVersionsAsync(CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Retrieves all module versions for a specific module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the module versions retrieval request.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> GetModuleVersionsByModuleIdAsync(Guid moduleId, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Retrieves a specific module version by its ID.
    /// </summary>
    /// <param name="moduleVersionId">The ID of the module version.</param>
    /// <param name="version">The version string of the module version.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the module version retrieval request.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> GetModuleVersionByIdAsync(Guid moduleVersionId, string version, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Creates a new module version for a specific module by its module ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="dto">The data transfer object containing the details for the module version creation.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the module version creation request.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> CreateModuleVersionAsync(Guid moduleId, ModuleVersionCreateDto dto, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Rolls back a specific module version for a specific module by its module ID and version string.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="version">The version string of the module version to roll back.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the module version rollback request.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> RollbackModuleVersionAsync(Guid moduleId, string version, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Updates an existing module version for a specific module by its module ID and version string.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="version">The version string of the module version to update.</param>
    /// <param name="dto">The data transfer object containing the updated details for the module version.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the module version update request.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> UpdateModuleVersionAsync(Guid moduleId, string version, ModuleVersionUpdateDto dto, CancellationToken ct = default, string? authToken = null);

    /// <summary>
    /// Deletes a specific module version for a specific module by its module ID and version string.
    /// </summary>
    /// <param name="moduleId">The ID of the module.</param>
    /// <param name="version">The version string of the module version to delete.</param>
    /// <param name="ct">Optional - The cancellation token.</param>
    /// <param name="authToken">Optional - The authentication token.</param>
    /// <returns>The HTTP response message resulting from the module version deletion request.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue
    /// or
    /// when the HTTP response indicates a failure.
    /// </exception>
    Task<HttpResponseMessage> DeleteModuleVersionAsync(Guid moduleId, string version, CancellationToken ct = default, string? authToken = null);
}