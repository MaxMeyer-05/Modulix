using Modulix.Client.Clients;

namespace Modulix.Client;

/// <inheritdoc cref="IModulixApi"/>
internal partial class ModulixApi : IModulixApi
{
    /// <summary>
    /// The HTTP client used for making requests to the Modulix API.
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <inheritdoc/>
    public IModuleManagementClient ModuleManagement => this;

    /// <inheritdoc/>
    public ISubEndpointManagementClient SubEndpointManagement => this;

    /// <inheritdoc/>
    public IScannerManagementClient ScannerManagement => this;

    /// <inheritdoc/>
    public IModuleVersionClient ModuleVersionManagement => this;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModulixApi"/> class with the specified <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">The <see cref="HttpClient"/> instance used to initialize the API clients.</param>
    public ModulixApi(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }
}