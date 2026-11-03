using Microsoft.AspNetCore.Http;

using System.ComponentModel.DataAnnotations;

namespace Modulix.Client.Models.Dtos;

/// <summary>
/// Represents a reusable Dockerfile template with its metadata.
/// </summary>
public class DockerfileTemplateDto
{
    /// <summary>
    /// The unique identifier of the Dockerfile template.
    /// </summary>
    public string Id { get; set; } = null!;

    /// <summary>
    /// The name of the Dockerfile template.
    /// </summary>
    public string FileName { get; set; } = null!;

    /// <summary>
    /// The storage path of the Dockerfile template.
    /// </summary>
    public string StoragePath { get; set; } = null!;

    /// <summary>
    /// The creation timestamp of the Dockerfile template.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The last update timestamp of the Dockerfile template.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Represents the data required to create a Dockerfile template.
/// </summary>
public class CreateDockerfileTemplateDto
{
    /// <summary>
    /// The name of the Dockerfile template.
    /// </summary>
    [Required(ErrorMessage = "FileName is required.")]
    public string FileName { get; set; } = null!;

    /// <summary>
    /// The content of the Dockerfile template.
    /// </summary>
    [Required(ErrorMessage = "Content is required.")]
    public IFormFile Content { get; set; } = null!;
}

/// <summary>
/// Represents the data required to update a Dockerfile template.
/// </summary>
public class UpdateDockerfileTemplateDto
{
    /// <summary>
    /// The name of the Dockerfile template.
    /// </summary>
    [Required(ErrorMessage = "FileName is required.")]
    public string FileName { get; set; } = null!;

    /// <summary>
    /// The content of the Dockerfile template.
    /// </summary>
    [Required(ErrorMessage = "Content is required.")]
    public IFormFile Content { get; set; } = null!;
}