using Modulix.Client.Clients;

namespace Modulix.Client;

/// <summary>
/// Represents the main entry point for interacting with the Modulix API.
/// </summary>
public interface IModulixApi
{
    /// <summary>
    /// Gets the client for managing modules within the Modulix API.
    /// </summary>
    IModuleManagementClient ModuleManagement { get; }
}