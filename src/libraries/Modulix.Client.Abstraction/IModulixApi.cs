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

    /// <summary>
    /// Gets the client for managing sub-endpoints within the Modulix API.
    /// </summary>
    ISubEndpointManagementClient SubEndpointManagement { get; }

    /// <summary>
    /// Gets the client for managing the scanner within the Modulix API.
    /// </summary>
    IScannerManagementClient ScannerManagement { get; }

    /// <summary>
    /// Gets the client for managing module versions within the Modulix API.
    /// </summary>
    IModuleVersionClient ModuleVersionManagement { get; }
}