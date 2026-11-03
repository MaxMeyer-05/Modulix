using Modulix.Client.Models.Dtos;
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
    /// Initializes a new instance of the <see cref="DockerfileTemplateService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public DockerfileTemplateService(ILogger<DockerfileTemplateService> logger)
    {
        _logger = logger;
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
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public async Task<DockerfileTemplateDto> GetDockerfileTemplateAsync(int id, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public async Task UpdateDockerfileTemplateAsync(UpdateDockerfileTemplateDto updateDockerfileTemplateDto, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}