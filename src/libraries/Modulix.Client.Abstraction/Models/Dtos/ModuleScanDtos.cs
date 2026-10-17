using System.ComponentModel.DataAnnotations;

using Modulix.Client.Models.Enums;

namespace Modulix.Client.Models.Dtos;

/// <summary>
/// Represents a discovered HTTP route from a scanned module assembly.
/// </summary>
public class DiscoveredEndpointDto
{
    /// <summary>
    /// The HTTP method.
    /// </summary>
    /// <value>e.g., GET, POST, PUT, DELETE</value>
    /// <example>GET</example>
    [Required]
    [MaxLength(10)]
    public string HttpMethod { get; set; } = null!;

    /// <summary>
    /// The relative endpoint route path.
    /// </summary>
    /// <value>e.g., /health, /items/{id}</value>
    /// <example>/health</example>
    [Required]
    [MaxLength(200)]
    public string EndpointPath { get; set; } = null!;
}

/// <summary>
/// Represents the result of scanning a module's extracted binaries.
/// </summary>
public class ModuleScanResultDto
{
    /// <summary>
    /// The file name of the primary entry-point assembly (e.g., "AnalyticsModule.dll").
    /// </summary>
    /// <example>AnalyticsModule.dll</example>
    /// <value>e.g., AnalyticsModule.dll</value>
    [Required]
    public string EntryAssemblyFileName { get; set; } = null!;

    /// <summary>
    /// The list of discovered controller endpoints.
    /// </summary>
    public List<DiscoveredEndpointDto> DiscoveredEndpoints { get; set; } = [];
}

/// <summary>
/// Result indicating if a scan was executed immediately or queued.
/// </summary>
public class ScanEnqueueResponse
{
    /// <summary>
    /// Indicates whether the scan job was queued or executed immediately.
    /// </summary>
    public bool WasQueued { get; set; }
    
    /// <summary>
    /// The result of the scan if it was executed immediately; otherwise, <c>null</c>.
    /// </summary>
    public ModuleScanResultDto? Result { get; set; }
}

/// <summary>
/// Represents an entry in the module endpoint scan queue.
/// </summary>
public class ModuleScanQueueItemDto
{
    /// <summary>
    /// The unique identifier of the module in the scan queue.
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// The display name of the module in the scan queue.
    /// </summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>
    /// The priority of the scan job.
    /// </summary>
    public ScanPriority Priority { get; set; }

    /// <summary>
    /// Indicates whether the scan job has already been dequeued and is currently running.
    /// </summary>
    public bool IsRunning { get; set; }
}