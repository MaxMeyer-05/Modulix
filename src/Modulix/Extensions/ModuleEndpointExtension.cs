using Modulix.Database.DbContexts;
using Modulix.Database.Entities;

using Modulix.Services.Interfaces;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

namespace Modulix.Extensions;

/// <summary>
/// Provides utility methods for validating and reconciling module endpoints.
/// </summary>
public class ModuleEndpointExtension
{
    /// <summary>
    /// The database context used to persist module endpoints and status.
    /// </summary>
    private readonly ServerContext _context;

    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<ModuleEndpointExtension> _logger;

    /// <summary>
    /// The Docker service used to provision module containers.
    /// </summary>
    private readonly IDockerService _dockerService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleEndpointExtension"/> class.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dockerService">The Docker service.</param>
    public ModuleEndpointExtension(
        ServerContext context,
        ILogger<ModuleEndpointExtension> logger,
        IDockerService dockerService)
    {
        _context = context;
        _logger = logger;
        _dockerService = dockerService;
    }

    /// <summary>
    /// Checks for conflicts with existing base endpoint paths.
    /// Throws an ArgumentException if a conflict is found.
    /// </summary>
    /// <param name="usedBasePaths">The list of currently used base endpoint paths.</param>
    /// <param name="normalizedBasePath">The normalized base endpoint path to check.</param>
    /// <exception cref="ArgumentException">Thrown if a conflict is found with existing base endpoint paths.</exception>
    public static void CheckBaseEndpointPathConflict(IEnumerable<string> usedBasePaths, string normalizedBasePath)
    {
        if (usedBasePaths.Contains(normalizedBasePath))
            throw new ArgumentException($"Base endpoint path '{normalizedBasePath}' is already in use.");
        else if (usedBasePaths.Any(ubp => ubp.StartsWith(normalizedBasePath)))
            throw new ArgumentException($"Base endpoint path '{normalizedBasePath}' conflicts with an existing base endpoint path.");
        else if (usedBasePaths.Any(ubp => normalizedBasePath.StartsWith(ubp)))
            throw new ArgumentException($"Base endpoint path '{normalizedBasePath}' is a sub-path of an existing base endpoint path.");
    }

    /// <summary>
    /// Creates a report detailing discrepancies between the initial and discovered endpoints for a module.
    /// </summary>
    /// <param name="module">The module entity containing the initially defined endpoints.</param>
    /// <param name="discoveredEndpoints">The list of endpoints discovered for the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A discrepancy report, or null if no discrepancies were found.</returns>
    public async Task<EndpointDiscrepancyReportDto?> CreateEndpointDiscrepancyReportAsync(
        Module module,
        List<DiscoveredEndpointDto> discoveredEndpoints,
        CancellationToken ct = default)
    {
        var initialEndpoints = module.SubEndpoints.ToList();
        EndpointDiscrepancyReportDto? report = null;

        if (initialEndpoints is null || initialEndpoints.Count == 0)
        {
            foreach (var scanned in discoveredEndpoints)
            {
                module.SubEndpoints.Add(new ModuleEndpoint
                {
                    ModuleId = module.Id,
                    HttpMethod = scanned.HttpMethod,
                    EndpointPath = scanned.EndpointPath,
                    Status = ModuleEndpointsStatus.Active
                });
            }

            module.Status = ModuleStatus.Created;
        }
        else
        {
            var matched = new List<DiscoveredEndpointDto>();
            var extra = new List<DiscoveredEndpointDto>();
            var missing = new List<DiscoveredEndpointDto>();

            foreach (var initial in initialEndpoints)
            {
                var found = discoveredEndpoints.Any(scanned =>
                    scanned.HttpMethod.Equals(
                        initial.HttpMethod,
                        StringComparison.OrdinalIgnoreCase)
                    && scanned.EndpointPath.Equals(
                        initial.EndpointPath,
                        StringComparison.OrdinalIgnoreCase)
                );

                if (found)
                {
                    initial.Status = ModuleEndpointsStatus.Active;
                    matched.Add(new DiscoveredEndpointDto { HttpMethod = initial.HttpMethod, EndpointPath = initial.EndpointPath });
                }
                else
                {
                    initial.Status = ModuleEndpointsStatus.PendingConfirmation;
                    missing.Add(new DiscoveredEndpointDto { HttpMethod = initial.HttpMethod, EndpointPath = initial.EndpointPath });
                }
            }

            foreach (var scanned in discoveredEndpoints)
            {
                var isExpected = initialEndpoints.Any(initial =>
                    initial.HttpMethod.Equals(
                        scanned.HttpMethod,
                        StringComparison.OrdinalIgnoreCase)
                    && initial.EndpointPath.Equals(
                        scanned.EndpointPath,
                        StringComparison.OrdinalIgnoreCase)
                );

                if (!isExpected)
                {
                    module.SubEndpoints.Add(new ModuleEndpoint
                    {
                        ModuleId = module.Id,
                        HttpMethod = scanned.HttpMethod,
                        EndpointPath = scanned.EndpointPath,
                        Status = ModuleEndpointsStatus.PendingConfirmation
                    });
                    extra.Add(scanned);
                }
            }

            bool hasDiscrepancy = extra.Count > 0 || missing.Count > 0;

            if (hasDiscrepancy)
            {
                report = new EndpointDiscrepancyReportDto
                {
                    ModuleId = module.Id,
                    HasDiscrepancy = true,
                    MatchedEndpoints = matched,
                    ExtraEndpoints = extra,
                    MissingEndpoints = missing
                };

                module.Status = ModuleStatus.PendingConfirmation;
            }
        }

        if (module.Status == ModuleStatus.Created)
        {
            try
            {
                module.Status = ModuleStatus.Starting;
                var containerId = await _dockerService.BuildContainerAsync(module.Id, module.StoragePath, module.ModuleEntryAssemblyFileName, module.ContainerPort, ct);
                module.ContainerId = containerId;
                await _dockerService.RunContainerAsync(containerId, ct);
                await _dockerService.WaitUntilReadyAsync(containerId, ct);
                module.Status = ModuleStatus.Running;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to provision Docker container for module {ModuleId}.", module.Id);
                module.Status = ModuleStatus.Failed;
            }
        }

        await _context.SaveChangesAsync(ct);
        return report;
    }
}