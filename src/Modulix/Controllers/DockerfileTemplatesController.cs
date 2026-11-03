using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using Modulix.Services.Interfaces;

namespace Modulix.Controllers;

/// <summary>
/// Manages reusable Dockerfile templates.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/dockerfiles")]
public class DockerfileTemplatesController : ControllerBase
{
    /// <summary>
    /// The service used to manage Dockerfile templates.
    /// </summary>
    private readonly IDockerfileTemplateService _dockerfileTemplateService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerfileTemplatesController"/> class.
    /// </summary>
    /// <param name="dockerfileTemplateService">The Dockerfile template service.</param>
    public DockerfileTemplatesController(IDockerfileTemplateService dockerfileTemplateService)
    {
        _dockerfileTemplateService = dockerfileTemplateService;
    }

    /// <summary>
    /// Creates a new Dockerfile template.
    /// </summary>
    /// <returns>The result of the creation operation.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateDockerfileTemplate()
    {
        return Created();
    }

    /// <summary>
    /// Retrieves a list of Dockerfile templates.
    /// </summary>
    /// <returns>A list of Dockerfile templates.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDockerfileTemplates()
    {
        return Ok();
    }

    /// <summary>
    /// Retrieves a specific Dockerfile template by its ID.
    /// </summary>
    /// <param name="id">The ID of the Dockerfile template.</param>
    /// <returns>The requested Dockerfile template.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDockerfileTemplateById(string id)
    {
        return Ok();
    }

    /// <summary>
    /// Updates a specific Dockerfile template by its ID.
    /// </summary>
    /// <param name="id">The ID of the Dockerfile template.</param>
    /// <returns>The result of the update operation.</returns>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDockerfileTemplate(string id)
    {
        return Ok();
    }

    /// <summary>
    /// Deletes a specific Dockerfile template by its ID.
    /// </summary>
    /// <param name="id">The ID of the Dockerfile template.</param>
    /// <returns>The result of the deletion operation.</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDockerfileTemplate(string id)
    {
        return Ok();
    }
}