using Microsoft.EntityFrameworkCore;

using Modulix.Client.Models.Dtos;

using Modulix.Mappers;
using Modulix.Extensions;
using Modulix.Database.DbContexts;
using Modulix.Services.Interfaces;

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
    /// The directory extension.
    /// </summary>
    private readonly DirectoryExtension _directoryExtension;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerfileTemplateService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="serverContext">The server context.</param>
    /// <param name="directoryExtension">The directory extension.</param>
    public DockerfileTemplateService(
        ILogger<DockerfileTemplateService> logger, 
        ServerContext serverContext,
        DirectoryExtension directoryExtension)
    {
        _logger = logger;
        _serverContext = serverContext;
        _directoryExtension = directoryExtension;
    }

    /// <inheritdoc />
    public async Task CreateDockerfileTemplateAsync(CreateDockerfileTemplateDto createDockerfileTemplateDto, CancellationToken ct = default)
    {
        var entity = createDockerfileTemplateDto.ToEntity();

        // ToDo: Handle creating the storage path for the Dockerfile template and saving the file content.

        entity.StoragePath = "placeholder/path";
        _serverContext.DockerfileTemplates.Add(entity);
        await _serverContext.SaveChangesAsync(ct);
        _logger.LogInformation("Created new Dockerfile template with ID {Id} in the database.", entity.Id);
    }

    /// <inheritdoc />
    public async Task DeleteDockerfileTemplateAsync(int id, CancellationToken ct = default)
    {
        var entity = await _serverContext.DockerfileTemplates.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
            throw new KeyNotFoundException($"Dockerfile template with ID {id} not found.");

        _directoryExtension.DeleteDirectory(entity.StoragePath);

        _serverContext.DockerfileTemplates.Remove(entity);
        await _serverContext.SaveChangesAsync(ct);
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

        entity.FileName = updateDockerfileTemplateDto.FileName;
        entity.Description = updateDockerfileTemplateDto.Description;

        // ToDo: Handle updating the Dockerfile template if the file content needs to be changed.

        await _serverContext.SaveChangesAsync(ct);
        _logger.LogInformation("Updated Dockerfile template with ID {Id} in the database.", id);
    }
}