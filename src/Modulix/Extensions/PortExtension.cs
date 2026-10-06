namespace Modulix.Extensions;

/// <summary>
/// Provides utility methods for selecting module container ports.
/// </summary>
public static class PortExtension
{
    /// <summary>
    /// Gets the selected port if it is unused, or the first available port in the range 1024-49151.
    /// </summary>
    /// <param name="usedPorts">The list of currently used ports.</param>
    /// <param name="selectedPort">The desired port, or 0 to automatically select an available port.</param>
    /// <returns>The available port.</returns>
    public static int GetAvailablePort(IEnumerable<int> usedPorts, int selectedPort)
    {
        int availablePort;
        if (selectedPort > 0)
        {
            if (usedPorts.Contains(selectedPort))
                throw new ArgumentException($"Container port '{selectedPort}' is already in use.");

            availablePort = selectedPort;
        }
        else
        {
            availablePort = Enumerable.Range(1024, 48127).Except(usedPorts).FirstOrDefault();
        }

        return availablePort;
    }
}