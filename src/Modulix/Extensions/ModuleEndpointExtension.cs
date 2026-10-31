using Modulix.Database.DbContexts;
using Modulix.Database.Entities;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;

using Modulix.Docker.Services.Interfaces;
using Modulix.Mappers;
using Modulix.ReverseProxy.Services;

namespace Modulix.Extensions;

/// <summary>
/// Provides utility methods for validating and reconciling module endpoints.
/// </summary>
public class ModuleEndpointExtension
{
    /// <summary>
    /// Reserved system URL prefixes that cannot be claimed or shadowed by modules.
    /// </summary>
    private static readonly string[] ReservedBasePaths =
    [
        "api/v1/modules",
        "api/v1/module-versions",
        "api/v1/endpoints",
        "api/v1/scanner",
        "api/v1/dockerfiles",
        "swagger",
        "openapi"
    ];

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
    private readonly IModuleDockerService _dockerService;

    /// <summary>
    /// The reverse proxy service used to synchronize module routing configurations.
    /// </summary>
    private readonly IReverseProxyService _reverseProxyService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleEndpointExtension"/> class.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dockerService">The Docker service.</param>
    /// <param name="reverseProxyService">The reverse proxy service.</param>
    public ModuleEndpointExtension(
        ServerContext context,
        ILogger<ModuleEndpointExtension> logger,
        IModuleDockerService dockerService,
        IReverseProxyService reverseProxyService)
    {
        _context = context;
        _logger = logger;
        _dockerService = dockerService;
        _reverseProxyService = reverseProxyService;
    }

    /// <summary>
    /// Checks for conflicts with reserved system prefixes and existing base endpoint paths.
    /// Throws an ArgumentException if a conflict is found.
    /// </summary>
    /// <param name="usedBasePaths">The list of currently used base endpoint paths.</param>
    /// <param name="normalizedBasePath">The normalized base endpoint path to check.</param>
    /// <exception cref="ArgumentException">Thrown if a conflict is found with reserved system prefixes or existing base endpoint paths.</exception>
    public static void CheckBaseUrlPathConflict(IEnumerable<string> usedBasePaths, string normalizedBasePath)
    {
        var candidatePath = normalizedBasePath.Trim().Trim('/');

        foreach (var reservedPath in ReservedBasePaths)
        {
            if (string.Equals(reservedPath, candidatePath, StringComparison.OrdinalIgnoreCase) ||
                reservedPath.StartsWith(candidatePath + "/", StringComparison.OrdinalIgnoreCase) ||
                candidatePath.StartsWith(reservedPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Base endpoint path '{normalizedBasePath}' conflicts with reserved system path '{reservedPath}'.");
            }
        }

        foreach (var ubp in usedBasePaths)
        {
            if (string.Equals(ubp, normalizedBasePath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Base endpoint path '{normalizedBasePath}' is already in use.");

            if (ubp.StartsWith(normalizedBasePath + "/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Base endpoint path '{normalizedBasePath}' conflicts with an existing base endpoint path '{ubp}'.");

            if (normalizedBasePath.StartsWith(ubp + "/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Base endpoint path '{normalizedBasePath}' is a sub-path of an existing base endpoint path '{ubp}'.");
        }
    }

    /// <summary>
    /// Marks all active sub-endpoints of the specified module as <see cref="ModuleEndpointsStatus.Inactive"/>,
    /// leaving endpoints in <see cref="ModuleEndpointsStatus.PendingConfirmation"/> untouched.
    /// </summary>
    /// <param name="module">The module entity whose active sub-endpoints should be deactivated.</param>
    public static void DeactivateActiveSubEndpoints(Module module)
    {
        foreach (var endpoint in module.SubEndpoints.Where(e => e.Status == ModuleEndpointsStatus.Active))
        {
            endpoint.Status = ModuleEndpointsStatus.Inactive;
        }
    }

    /// <summary>
    /// Determines whether the specified sub-endpoint existed in the module's <see cref="Module.CurrentVersion"/>.
    /// </summary>
    /// <param name="module">The parent module entity.</param>
    /// <param name="endpoint">The sub-endpoint entity to evaluate.</param>
    /// <returns><c>true</c> if the endpoint belongs to the current or an earlier version; otherwise, <c>false</c>.</returns>
    public static bool ExistedInCurrentVersion(Module module, ModuleEndpoint endpoint)
    {
        var currentVersion = module.Versions.FirstOrDefault(v => v.Version == module.CurrentVersion);
        if (currentVersion is null)
            return true;

        var endpointVersion = endpoint.ModuleVersion
            ?? module.Versions.FirstOrDefault(v => v.Id == endpoint.ModuleVersionId);

        var isSameVersion = ReferenceEquals(endpointVersion, currentVersion) ||
            string.Equals(endpointVersion?.Version, currentVersion.Version, StringComparison.Ordinal) ||
            (endpointVersion is not null && currentVersion.Id != 0 && endpointVersion.Id == currentVersion.Id);

        return endpointVersion is null ||
            isSameVersion ||
            endpointVersion.ReleasedAtUtc < currentVersion.ReleasedAtUtc ||
            (endpointVersion.ReleasedAtUtc == currentVersion.ReleasedAtUtc && endpointVersion.Id <= currentVersion.Id);
    }

    /// <summary>
    /// Restores confirmed sub-endpoints belonging to the module's <see cref="Module.CurrentVersion"/>
    /// to <see cref="ModuleEndpointsStatus.Active"/>, while marking endpoints from newer versions as
    /// <see cref="ModuleEndpointsStatus.Inactive"/> and leaving <see cref="ModuleEndpointsStatus.PendingConfirmation"/> endpoints untouched.
    /// </summary>
    /// <param name="module">The module entity whose sub-endpoints should be activated.</param>
    public static void ActivateCurrentVersionSubEndpoints(Module module)
    {
        foreach (var endpoint in module.SubEndpoints.Where(e => e.Status != ModuleEndpointsStatus.PendingConfirmation))
        {
            endpoint.Status = ExistedInCurrentVersion(module, endpoint)
                ? ModuleEndpointsStatus.Active
                : ModuleEndpointsStatus.Inactive;
        }
    }

    /// <summary>
    /// Synchronizes the reverse proxy configuration for the specified module based on its current status and active sub-endpoints.
    /// </summary>
    /// <param name="module">The module entity to synchronize.</param>
    public void SynchronizeReverseProxy(Module module)
    {
        if (module.Status == ModuleStatus.Running)
        {
            _reverseProxyService.UpsertModule(module.ToProxyModuleDefinition());
        }
        else
        {
            _reverseProxyService.RemoveModule(module.Id);
        }
    }

    /// <summary>
    /// Creates a report detailing discrepancies between the initial and discovered endpoints for a module.
    /// </summary>
    /// <param name="module">The module entity containing the initially defined endpoints.</param>
    /// <param name="discoveredEndpoints">The list of endpoints discovered for the module.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A discrepancy report, or null if no discrepancies were found.</returns>
    public async Task<SubEndpointDiscrepancyReportDto?> CreateEndpointDiscrepancyReportAsync(
        Module module,
        List<DiscoveredSubEndpointDto> discoveredEndpoints,
        CancellationToken ct = default)
    {
        var currentVersion = module.Versions.FirstOrDefault(v => v.Version == module.CurrentVersion)
            ?? throw new InvalidOperationException($"Storage path for module '{module.Id}' version '{module.CurrentVersion}' was not found.");

        var initialEndpoints = module.SubEndpoints.ToList();
        SubEndpointDiscrepancyReportDto? report = null;

        if (initialEndpoints.Count == 0)
        {
            foreach (var scanned in discoveredEndpoints)
            {
                module.SubEndpoints.Add(new ModuleEndpoint
                {
                    ModuleId = module.Id,
                    ModuleVersionId = currentVersion.Id,
                    ModuleVersion = currentVersion,
                    HttpMethod = scanned.HttpMethod,
                    EndpointPath = scanned.EndpointPath,
                    Status = ModuleEndpointsStatus.Active
                });
            }
            module.Status = ModuleStatus.Created;
        }
        else
        {
            var matched = new List<DiscoveredSubEndpointDto>();
            var extra = new List<DiscoveredSubEndpointDto>();
            var missing = new List<DiscoveredSubEndpointDto>();

            foreach (var initial in initialEndpoints)
            {
                var found = discoveredEndpoints.Any(scanned =>
                    scanned.HttpMethod.Equals(initial.HttpMethod, StringComparison.OrdinalIgnoreCase) &&
                    scanned.EndpointPath.Equals(initial.EndpointPath, StringComparison.OrdinalIgnoreCase));

                if (found)
                {
                    initial.Status = ModuleEndpointsStatus.Active;
                    matched.Add(new DiscoveredSubEndpointDto { HttpMethod = initial.HttpMethod, EndpointPath = initial.EndpointPath });
                }
                else
                {
                    initial.Status = ModuleEndpointsStatus.PendingConfirmation;
                    missing.Add(new DiscoveredSubEndpointDto { HttpMethod = initial.HttpMethod, EndpointPath = initial.EndpointPath });
                }
            }

            foreach (var scanned in discoveredEndpoints)
            {
                var isExpected = initialEndpoints.Any(initial =>
                    initial.HttpMethod.Equals(scanned.HttpMethod, StringComparison.OrdinalIgnoreCase) &&
                    initial.EndpointPath.Equals(scanned.EndpointPath, StringComparison.OrdinalIgnoreCase));

                if (!isExpected)
                {
                    module.SubEndpoints.Add(new ModuleEndpoint
                    {
                        ModuleId = module.Id,
                        ModuleVersionId = currentVersion.Id,
                        ModuleVersion = currentVersion,
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
                report = new SubEndpointDiscrepancyReportDto
                {
                    ModuleId = module.Id,
                    HasDiscrepancy = true,
                    MatchedEndpoints = matched,
                    ExtraEndpoints = extra,
                    MissingEndpoints = missing
                };
                module.Status = ModuleStatus.PendingConfirmation;
            }
            else
            {
                module.Status = ModuleStatus.Created;
            }
        }

        if (module.Status == ModuleStatus.Created)
        {
            try
            {
                module.Status = ModuleStatus.Starting;
                var containerId = await _dockerService.BuildModuleContainerAsync(
                    module.Id,
                    currentVersion.StoragePath,
                    currentVersion.ModuleEntryAssemblyFileName,
                    module.ContainerPort,
                    ct);
                module.ContainerId = containerId;
                await _dockerService.RunModuleContainerAsync(containerId, ct);
                await _dockerService.WaitUntilReadyAsync(containerId, ct);
                module.Status = ModuleStatus.Running;
                ActivateCurrentVersionSubEndpoints(module);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to provision Docker container for module {ModuleId}.", module.Id);
                module.Status = ModuleStatus.Failed;
                DeactivateActiveSubEndpoints(module);
            }
        }

        await _context.SaveChangesAsync(ct);
        SynchronizeReverseProxy(module);
        return report;
    }
}