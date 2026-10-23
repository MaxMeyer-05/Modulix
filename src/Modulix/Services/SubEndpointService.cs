using Microsoft.EntityFrameworkCore;

using Modulix.Mappers;

using Modulix.Client.Models.Dtos;
using Modulix.Client.Models.Dtos.Query;

using Modulix.Client.Models.Enums;

using Modulix.Extensions;
using Modulix.Docker.Services;
using Modulix.Database.DbContexts;
using Modulix.Services.Interfaces;

namespace Modulix.Services;

/// <inheritdoc cref="ISubEndpointService"/>
public class SubEndpointService : ISubEndpointService
{
    /// <summary>
    /// The database context used by the service.
    /// </summary>
    private readonly ServerContext _context;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<SubEndpointService> _logger;

    /// <summary>
    /// The Docker service.
    /// </summary>
    private readonly IDockerService _dockerService;

    /// <summary>
    /// The module endpoint extension for validating and reconciling endpoints.
    /// </summary>
    private readonly ModuleEndpointExtension _moduleEndpointExtension;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubEndpointService"/> class.
    /// </summary>
    /// <param name="context">The database context used by the service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="dockerService">The Docker service.</param>
    /// <param name="moduleEndpointExtension">The extension for validating and reconciling module endpoints.</param>
    public SubEndpointService(
        ServerContext context, 
        ILogger<SubEndpointService> logger,
        IDockerService dockerService,
        ModuleEndpointExtension moduleEndpointExtension)
    {
        _context = context;
        _logger = logger;
        _dockerService = dockerService;
        _moduleEndpointExtension = moduleEndpointExtension;
    }

    /// <inheritdoc/>
    public async Task<ModuleDetailDto> ConfirmSubEndpointsAsync(Guid moduleId, ConfirmEndpointsDto dto, CancellationToken ct = default)
    {
        var module = await _context.Modules
            .Include(m => m.SubEndpoints)
            .Include(m => m.Versions)
            .SingleOrDefaultAsync(m => m.Id == moduleId, ct);

        if (module is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        if (module.Status != ModuleStatus.PendingConfirmation)
            throw new InvalidOperationException($"Module '{moduleId}' is not in PendingConfirmation status.");

        var previousEndpoints = module.SubEndpoints
            .Select(endpoint => (Endpoint: endpoint, endpoint.Status))
            .ToList();

        var pendingEndpoints = module.SubEndpoints
            .Where(e => e.Status == ModuleEndpointsStatus.PendingConfirmation)
            .ToList();

        foreach (var endpoint in pendingEndpoints)
        {
            // Check if the pending endpoint is confirmed as an extra endpoint
            if (dto.ConfirmedEndpoints.Any(ce => ce.ExtraEndpoints?.Any(ee =>
                ee.HttpMethod == endpoint.HttpMethod &&
                ee.EndpointPath.Equals(endpoint.EndpointPath, StringComparison.OrdinalIgnoreCase)) == true))
            {
                endpoint.Status = ModuleEndpointsStatus.Active;
            }
            // Check if the pending endpoint is confirmed as a missing endpoint
            else if (dto.ConfirmedEndpoints.Any(ce => ce.MissingEndpoints?.Any(me =>
                me.HttpMethod == endpoint.HttpMethod &&
                me.EndpointPath.Equals(endpoint.EndpointPath, StringComparison.OrdinalIgnoreCase)) == true))
            {
                _context.ModuleEndpoints.Remove(endpoint);
            }
        }

        if (dto.ConfirmedEndpoints.Count == 0 || pendingEndpoints.Any(endpoint =>
                endpoint.Status == ModuleEndpointsStatus.PendingConfirmation &&
                _context.Entry(endpoint).State != EntityState.Deleted))
        {
            await _context.SaveChangesAsync(ct);
            return module.ToModuleDetailDto();
        }

        module.Status = ModuleStatus.Created;
        var previousContainerId = module.ContainerId;
        string? candidateId = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(previousContainerId))
            {
                await _dockerService.TryRemoveModuleContainerAsync(previousContainerId, module.Id);
                module.ContainerId = null;
            }

            var currentVersion = module.Versions.FirstOrDefault(v => v.Version == module.CurrentVersion)
                ?? throw new InvalidOperationException($"Storage path for module '{module.Id}' version '{module.CurrentVersion}' was not found.");

            module.Status = ModuleStatus.Starting;
            candidateId = await _dockerService.BuildContainerAsync(
                module.Id,
                currentVersion.StoragePath,
                currentVersion.ModuleEntryAssemblyFileName,
                module.ContainerPort,
                ct);
            module.ContainerId = candidateId;

            await _dockerService.RunContainerAsync(candidateId, ct);
            await _dockerService.WaitUntilReadyAsync(candidateId, ct);
            module.Status = ModuleStatus.Running;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start confirmed module container {ModuleId}.", module.Id);
            module.Status = ModuleStatus.Failed;
        }

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch
        {
            if (candidateId is not null)
                await _dockerService.TryRemoveModuleContainerAsync(candidateId, module.Id);

            module.ContainerId = previousContainerId;
            module.Status = ModuleStatus.PendingConfirmation;
            _context.Entry(module).State = EntityState.Unchanged;

            foreach (var previousEndpoint in previousEndpoints)
            {
                previousEndpoint.Endpoint.Status = previousEndpoint.Status;
                _context.Entry(previousEndpoint.Endpoint).State = EntityState.Unchanged;
                if (!module.SubEndpoints.Contains(previousEndpoint.Endpoint))
                    module.SubEndpoints.Add(previousEndpoint.Endpoint);
            }
            throw;
        }

        _moduleEndpointExtension.SynchronizeReverseProxy(module);
        return module.ToModuleDetailDto();
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<ModuleSubEndpointsDetailDto>> GetAllSubEndpointsDetailAsync(SubEndpointQueryDto? query = null, PaginationQueryDto? pagination = null, CancellationToken ct = default)
    {
        pagination ??= new PaginationQueryDto();
        var page = pagination.Page < 1 ? PaginationQueryDto.DefaultPage : pagination.Page;
        var pageSize = pagination.PageSize < 1
            ? PaginationQueryDto.DefaultPageSize
            : Math.Min(pagination.PageSize, PaginationQueryDto.MaxPageSize);

        var subEndpointQuery = _context.ModuleEndpoints.AsNoTracking();
        if (query is not null)
        {
            if (query.HttpMethod is not null)
                subEndpointQuery = subEndpointQuery.Where(se => se.HttpMethod == query.HttpMethod);

            if (query.Status is not null)
                subEndpointQuery = subEndpointQuery.Where(se => se.Status == query.Status);
        }

        var totalCount = await subEndpointQuery.CountAsync(ct);
        var subEndpointsDetail = await subEndpointQuery
            .Include(se => se.Module)
            .Include(se => se.ModuleVersion)
            .OrderByDescending(se => se.CreatedAtUtc)
            .ThenBy(se => se.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var subEndpointsDetailDtos = subEndpointsDetail.Select(se => se.ToModuleSubEndpointsDetailDto()).ToList();

        _logger.LogDebug("Retrieved {Count} detailed sub-endpoints from the database.", subEndpointsDetailDtos.Count);

        return new PagedResultDto<ModuleSubEndpointsDetailDto>
        {
            Items = subEndpointsDetailDtos,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<ModuleSubEndpointDto>> GetModuleSubEndpointsAsync(Guid moduleId, SubEndpointQueryDto? query = null, PaginationQueryDto? pagination = null, CancellationToken ct = default)
    {
        var exists = await _context.Modules.FindAsync([moduleId], ct);
        if (exists is null)
            throw new KeyNotFoundException($"Module with ID '{moduleId}' was not found.");

        pagination ??= new PaginationQueryDto();
        var page = pagination.Page < 1 ? PaginationQueryDto.DefaultPage : pagination.Page;
        var pageSize = pagination.PageSize < 1
            ? PaginationQueryDto.DefaultPageSize
            : Math.Min(pagination.PageSize, PaginationQueryDto.MaxPageSize);

        var subEndpointQuery = _context.ModuleEndpoints
            .AsNoTracking()
            .Where(e => e.ModuleId == moduleId);

        if (query is not null)
        {
            if (query.HttpMethod is not null)
                subEndpointQuery = subEndpointQuery.Where(se => se.HttpMethod == query.HttpMethod);

            if (query.Status is not null)
                subEndpointQuery = subEndpointQuery.Where(se => se.Status == query.Status);
        }

        var totalCount = await subEndpointQuery.CountAsync(ct);
        var subEndpoints = await subEndpointQuery
            .Include(se => se.ModuleVersion)
            .OrderByDescending(e => e.CreatedAtUtc)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var subEndpointDtos = subEndpoints.Select(se => se.ToSubEndpointDto()).ToList();

        _logger.LogDebug("Retrieved {Count} sub-endpoints for module with ID '{ModuleId}'.", subEndpoints.Count, moduleId);
        return new PagedResultDto<ModuleSubEndpointDto>
        {
            Items = subEndpointDtos,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}