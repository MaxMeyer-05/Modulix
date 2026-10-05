namespace Server.Models.Enums;

/// <summary>
/// Represents the status of a module endpoint.
/// </summary>
public enum ModuleEndpointsStatus
{
    /// <summary>
    /// The module endpoint is pending confirmation.
    /// </summary>
    PendingConfirmation,

    /// <summary>
    /// The module endpoint is active.
    /// </summary>
    Active,

    /// <summary>
    /// The module endpoint is inactive.
    /// </summary>
    Inactive
}