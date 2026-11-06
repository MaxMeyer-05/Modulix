using Modulix.Client.Models.Dtos;

namespace Modulix.Web.Models;

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
