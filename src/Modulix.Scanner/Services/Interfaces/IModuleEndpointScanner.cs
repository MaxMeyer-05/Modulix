using Modulix.Scanner.Models;

namespace Modulix.Scanner.Services.Interfaces;

/// <summary>
/// Provides functionality for discovering runnable entry points and HTTP endpoints 
/// from compiled module assemblies without acquiring permanent file locks.
/// </summary>
public interface IModuleEndpointScanner
{
    /// <summary>
    /// Scans the specified module storage directory, extracts exposed controller endpoints,
    /// and resolves the entry-point assembly.
    /// </summary>
    /// <param name="moduleDirectoryPath">The physical directory path where the module's binaries reside.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A <see cref="ModuleScanResultDto"/> containing the entry assembly name and endpoints.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown if the target directory does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if no runnable entry point can be identified or not all assembly types can be loaded.</exception>
    /// <exception cref="FileNotFoundException">Thrown if no runtime config files or entry assembly DLL is found.</exception>
    Task<ModuleScanResultDto> ScanDirectoryAsync(string moduleDirectoryPath, CancellationToken ct = default);
}