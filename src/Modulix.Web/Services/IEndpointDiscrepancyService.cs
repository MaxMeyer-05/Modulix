using Modulix.Client.Models.Dtos;

namespace Modulix.Web.Services;

/// <summary>
/// Categorizes a module sub-endpoint according to the latest <see cref="SubEndpointDiscrepancyReportDto"/>.
/// </summary>
public enum EndpointDiscrepancyKind
{
    /// <summary>
    /// No discrepancy classification is recorded for the endpoint.
    /// </summary>
    None,

    /// <summary>
    /// The endpoint was specified by the user and also found by the scanner (<see cref="SubEndpointDiscrepancyReportDto.MatchedEndpoints"/>).
    /// </summary>
    Matched,

    /// <summary>
    /// The endpoint was discovered by the scanner but was not specified by the user (<see cref="SubEndpointDiscrepancyReportDto.ExtraEndpoints"/>).
    /// </summary>
    Extra,

    /// <summary>
    /// The endpoint was specified by the user but was not found by the scanner (<see cref="SubEndpointDiscrepancyReportDto.MissingEndpoints"/>).
    /// </summary>
    Missing
}

/// <summary>
/// Preserves <see cref="SubEndpointDiscrepancyReportDto"/> instances returned during module creation or version updates
/// so the Web UI can display discrepancy details (<c>ExtraEndpoints</c>, <c>MissingEndpoints</c>, and <c>MatchedEndpoints</c>).
/// </summary>
public interface IEndpointDiscrepancyService
{
    /// <summary>
    /// Retrieves the stored discrepancy report for the specified module, if available.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <returns>The stored <see cref="SubEndpointDiscrepancyReportDto"/>, or <see langword="null"/> if none is stored.</returns>
    SubEndpointDiscrepancyReportDto? GetReport(Guid moduleId);

    /// <summary>
    /// Stores or replaces the discrepancy report for the specified module.
    /// If <paramref name="report"/> is <see langword="null"/> or has no discrepancy, any existing report for the module is cleared.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="report">The discrepancy report returned by the backend.</param>
    void SaveReport(Guid moduleId, SubEndpointDiscrepancyReportDto? report);

    /// <summary>
    /// Removes resolved endpoints from the stored discrepancy report for the specified module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    /// <param name="resolvedEndpoints">The endpoints that were confirmed or removed.</param>
    void RemoveEndpoints(Guid moduleId, IEnumerable<DiscoveredSubEndpointDto> resolvedEndpoints);

    /// <summary>
    /// Clears any stored discrepancy report for the specified module.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module.</param>
    void ClearReport(Guid moduleId);
}

