using Microsoft.EntityFrameworkCore;

using Modulix.Client.Models.Dtos;

using Modulix.Database.DbContexts;
using Modulix.Mappers;
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
    /// Initializes a new instance of the <see cref="DockerfileTemplateService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="serverContext">The server context.</param>
    public DockerfileTemplateService(
        ILogger<DockerfileTemplateService> logger, 
        ServerContext serverContext)
    {
        _logger = logger;
        _serverContext = serverContext;
    }

    /// <inheritdoc />
    public async Task CreateDockerfileTemplateAsync(CreateDockerfileTemplateDto createDockerfileTemplateDto, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public async Task DeleteDockerfileTemplateAsync(int id, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public async Task<IList<DockerfileTemplateDto>> GetAllDockerfileTemplatesAsync(CancellationToken ct = default)
    {
        var entities = await _serverContext.DockerfileTemplates.ToListAsync(ct);
        return entities.Select(entity => entity.ToDto()).ToList();
    }

    /// <inheritdoc />
    public async Task<DockerfileTemplateDto> GetDockerfileTemplateAsync(int id, CancellationToken ct = default)
    {
        var entity = await _serverContext.DockerfileTemplates.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
        {
            throw new KeyNotFoundException($"Dockerfile template with ID {id} not found.");
        }
        return entity.ToDto();
    }

    /// <inheritdoc />
    public async Task UpdateDockerfileTemplateAsync(UpdateDockerfileTemplateDto updateDockerfileTemplateDto, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}