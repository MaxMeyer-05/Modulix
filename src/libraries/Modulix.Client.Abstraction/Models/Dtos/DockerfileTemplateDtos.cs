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
    public int Id { get; set; }

    /// <summary>
    /// The name of the Dockerfile template.
    /// </summary>
    public string FileName { get; set; } = null!;

    /// <summary>
    /// The description of the Dockerfile template.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the template is supplied by Modulix rather than created by a user.
    /// </summary>
    public bool IsBuiltIn { get; set; }

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
    [MaxLength(255, ErrorMessage = "FileName cannot exceed 255 characters.")]
    public string FileName { get; set; } = null!;

    /// <summary>
    /// The description of the Dockerfile template.
    /// </summary>
    [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

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
    [MaxLength(255, ErrorMessage = "FileName cannot exceed 255 characters.")]
    public string FileName { get; set; } = null!;

    /// <summary>
    /// The description of the Dockerfile template.
    /// </summary>
    [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    /// <summary>
    /// The content of the Dockerfile template.
    /// </summary>
    [Required(ErrorMessage = "Content is required.")]
    public IFormFile Content { get; set; } = null!;
}