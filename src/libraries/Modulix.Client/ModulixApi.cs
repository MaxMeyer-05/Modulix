using Modulix.Client.Clients;

namespace Modulix.Client;

/// <inheritdoc cref="IModulixApi"/>
internal partial class ModulixApi : IModulixApi
{
    private readonly HttpClient _httpClient;

    /// <inheritdoc/>
    public IModuleManagementClient ModuleManagement => this;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModulixApi"/> class with the specified <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">The <see cref="HttpClient"/> instance used to initialize the API clients.</param>
    public ModulixApi(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }
}