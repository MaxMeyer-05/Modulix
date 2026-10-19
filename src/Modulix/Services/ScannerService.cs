using Microsoft.EntityFrameworkCore;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Enums;


using Modulix.Extensions;
using Modulix.Database.DbContexts;
using Modulix.Services.Interfaces;

namespace Modulix.Services;

/// <inheritdoc cref="IScannerService"/>
public class ScannerService : IScannerService
{
    /// <summary>
    /// The database context used by the service.
    /// </summary>
    private readonly ServerContext _context;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<ScannerService> _logger;

    /// <summary>
    /// The module endpoint scanner.
    /// </summary>
    private readonly IModuleEndpointScanner _endpointScanner;

    /// <summary>
    /// The module endpoint extension for validating and reconciling endpoints.
    /// </summary>
    private readonly ModuleEndpointExtension _moduleEndpointExtension;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScannerService"/> class.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="endpointScanner">The module endpoint scanner.</param>
    /// <param name="moduleEndpointExtension">The module endpoint extension.</param>
    public ScannerService(
        ServerContext context, 
        ILogger<ScannerService> logger, 
        IModuleEndpointScanner endpointScanner, 
        ModuleEndpointExtension moduleEndpointExtension)
    {
        _context = context;
        _logger = logger;
        _endpointScanner = endpointScanner;
        _moduleEndpointExtension = moduleEndpointExtension;
    }

    /// <inheritdoc/>
    public async Task ProcessQueuedScanResultAsync(Guid moduleId, ModuleScanResultDto result, CancellationToken ct = default)
    {
        var module = await _context.Modules.Include(m => m.SubEndpoints).FirstOrDefaultAsync(m => m.Id == moduleId, ct);
        if (module == null || module.Status != ModuleStatus.QueuedForScan) return;

        module.ModuleEntryAssemblyFileName = result.EntryAssemblyFileName;
        module.Status = ModuleStatus.Created;
        await _moduleEndpointExtension.CreateEndpointDiscrepancyReportAsync(module, result.DiscoveredEndpoints, ct);

        _logger.LogInformation("Processed queued scan result for module with ID '{ModuleId}'.", moduleId);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ModuleScanQueueItemDto>> GetScanQueueAsync(CancellationToken ct = default)
    {
        var queuedScans = _endpointScanner.GetQueuedScans();
        if (queuedScans.Count == 0)
            return [];

        var moduleIds = queuedScans.Select(item => item.ModuleId).Distinct().ToList();
        var moduleNames = await _context.Modules
            .AsNoTracking()
            .Where(module => moduleIds.Contains(module.Id))
            .ToDictionaryAsync(module => module.Id, module => module.ModuleName, ct);

        _logger.LogDebug("Retrieved {Count} queued scans from the endpoint scanner.", queuedScans.Count);

        return queuedScans.Select(item => new ModuleScanQueueItemDto
        {
            ModuleId = item.ModuleId,
            ModuleName = moduleNames.GetValueOrDefault(item.ModuleId, string.Empty),
            Priority = item.Priority,
            IsRunning = item.IsRunning
        }).ToList();
    }
}