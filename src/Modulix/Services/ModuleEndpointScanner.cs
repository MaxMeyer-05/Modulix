using Modulix.Models.Dtos;
using Modulix.Services.Interfaces;

namespace Modulix.Services;

/// <inheritdoc cref="IModuleEndpointScanner"/>
public class ModuleEndpointScanner : IModuleEndpointScanner
{
    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<ModuleEndpointScanner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleEndpointScanner"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public ModuleEndpointScanner(ILogger<ModuleEndpointScanner> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<ModuleScanResultDto> ScanDirectoryAsync(string moduleDirectoryPath, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}