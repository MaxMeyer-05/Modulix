using Microsoft.EntityFrameworkCore;

using Modulix.Client.Models.Dtos;

using Modulix.Mappers;
using Modulix.Database.DbContexts;

using Modulix.Services.Interfaces;
using Modulix.Services.Storage.Interfaces;

namespace Modulix.Services;

/// <inheritdoc cref="IDockerfileTemplateService" />
public class DockerfileTemplateService : IDockerfileTemplateService
{
    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<DockerfileTemplateService> _logger;

    /// <summary>
    /// The server context.
    /// </summary>
    private readonly ServerContext _serverContext;

    /// <summary>
    /// The storage service for Dockerfile templates.
    /// </summary>
    private readonly IDockerfileTemplateStorageService _templateStorage;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerfileTemplateService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="serverContext">The server context.</param>
    /// <param name="templateStorage">The Dockerfile template storage service.</param>
    public DockerfileTemplateService(
        ILogger<DockerfileTemplateService> logger, 
        ServerContext serverContext,
        IDockerfileTemplateStorageService templateStorage)
    {
        _logger = logger;
        _serverContext = serverContext;
        _templateStorage = templateStorage;
    }

    /// <inheritdoc />
    public async Task CreateDockerfileTemplateAsync(CreateDockerfileTemplateDto createDockerfileTemplateDto, CancellationToken ct = default)
    {
        var entity = createDockerfileTemplateDto.ToEntity();

        entity.StoragePath = await _templateStorage.SaveAsync(createDockerfileTemplateDto.Content, ct);
        _serverContext.DockerfileTemplates.Add(entity);
        try
        {
            await _serverContext.SaveChangesAsync(ct);
        }
        catch
        {
            _serverContext.DockerfileTemplates.Remove(entity);
            _templateStorage.Delete(entity.StoragePath);
            throw;
        }
        _logger.LogInformation("Created new Dockerfile template with ID {Id} in the database.", entity.Id);
    }

    /// <inheritdoc />
    public async Task DeleteDockerfileTemplateAsync(int id, CancellationToken ct = default)
    {
        var entity = await _serverContext.DockerfileTemplates.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
            throw new KeyNotFoundException($"Dockerfile template with ID {id} not found.");

        _serverContext.DockerfileTemplates.Remove(entity);
        try
        {
            await _serverContext.SaveChangesAsync(ct);
        }
        catch
        {
            _serverContext.Entry(entity).State = EntityState.Unchanged;
            throw;
        }

        _templateStorage.Delete(entity.StoragePath);
        _logger.LogInformation("Deleted Dockerfile template with ID {Id} from the storage and database.", id);
    }

    /// <inheritdoc />
    public async Task<IList<DockerfileTemplateDto>> GetAllDockerfileTemplatesAsync(CancellationToken ct = default)
    {
        var entities = await _serverContext.DockerfileTemplates.ToListAsync(ct);
        _logger.LogDebug("Retrieved {Count} Dockerfile templates from the database.", entities.Count);
        return entities.Select(entity => entity.ToDto()).ToList();
    }

    /// <inheritdoc />
    public async Task<DockerfileTemplateDto> GetDockerfileTemplateAsync(int id, CancellationToken ct = default)
    {
        var entity = await _serverContext.DockerfileTemplates.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
            throw new KeyNotFoundException($"Dockerfile template with ID {id} not found.");

        _logger.LogDebug("Retrieved Dockerfile template with ID {Id} from the database.", id);

        return entity.ToDto();
    }

    /// <inheritdoc />
    public async Task UpdateDockerfileTemplateAsync(int id, UpdateDockerfileTemplateDto updateDockerfileTemplateDto, CancellationToken ct = default)
    {
        var entity = await _serverContext.DockerfileTemplates.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
            throw new KeyNotFoundException($"Dockerfile template with ID {id} not found.");

        var oldStoragePath = entity.StoragePath;
        var oldFileName = entity.FileName;
        var oldDescription = entity.Description;
        var newStoragePath = await _templateStorage.SaveAsync(updateDockerfileTemplateDto.Content, ct);
        entity.FileName = updateDockerfileTemplateDto.FileName;
        entity.Description = updateDockerfileTemplateDto.Description;
        entity.StoragePath = newStoragePath;
        try
        {
            await _serverContext.SaveChangesAsync(ct);
        }
        catch
        {
            entity.FileName = oldFileName;
            entity.Description = oldDescription;
            entity.StoragePath = oldStoragePath;
            _serverContext.Entry(entity).State = EntityState.Unchanged;
            _templateStorage.Delete(newStoragePath);
            throw;
        }

        _templateStorage.Delete(oldStoragePath);
        _logger.LogInformation("Updated Dockerfile template with ID {Id} in the database.", id);
    }
}