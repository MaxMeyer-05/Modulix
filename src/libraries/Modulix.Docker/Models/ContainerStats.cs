namespace Modulix.Docker.Models;

/// <summary>
/// Represents runtime resource metrics and live state for a Docker container.
/// </summary>
public sealed class ContainerStats
{
    /// <summary>
    /// Indicates whether the container is currently running.
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// The CPU usage percentage of the container.
    /// </summary>
    public double CpuUsagePercent { get; set; }

    /// <summary>
    /// The current memory usage of the container in MiB.
    /// </summary>
    public double MemoryUsageMiB { get; set; }

    /// <summary>
    /// The memory limit of the container in MiB.
    /// </summary>
    public double MemoryLimitMiB { get; set; }

    /// <summary>
    /// The container uptime in seconds when running.
    /// </summary>
    public double UptimeSeconds { get; set; }

    /// <summary>
    /// The exit code of the container's last execution.
    /// </summary>
    public long ExitCode { get; set; }
}

