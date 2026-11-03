using Modulix.Client.Clients;

namespace Modulix.Client;

/// <summary>
/// Represents the main entry point for interacting with the Modulix API.
/// </summary>
public interface IModulixApi
{
    /// <summary>
    /// Gets the client for interacting with modules.
    /// </summary>
    IModuleClient Module { get; }

    /// <summary>
    /// Gets the client for interacting with sub-endpoints.
    /// </summary>
    ISubEndpointClient SubEndpoint { get; }

    /// <summary>
    /// Gets the client for interacting with the scanner.
    /// </summary>
    IScannerClient Scanner { get; }

    /// <summary>
    /// Gets the client for interacting with module versions.
    /// </summary>
    IModuleVersionClient ModuleVersion { get; }
}