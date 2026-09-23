namespace Server.Models.Enums;

/// <summary>
/// Represents the status of a module.
/// </summary>
public enum ModuleStatus
{
    /// <summary>
    /// The module has been created but not yet started.
    /// </summary>
    Created,

    /// <summary>
    /// The module is in the process of starting.
    /// </summary>
    Starting,

    /// <summary>
    /// The module is currently running.
    /// </summary>
    Running,

    /// <summary>
    /// The module has been stopped.
    /// </summary>
    Stopped,

    /// <summary>
    /// The module has encountered a failure.
    /// </summary>
    Failed
}