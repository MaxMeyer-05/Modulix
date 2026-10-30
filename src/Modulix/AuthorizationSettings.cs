using System.Net;
using System.Net.Sockets;

using System.ComponentModel.DataAnnotations;

namespace Modulix;

/// <summary>
/// Settings for authorization configuration.
/// </summary>
/// <remarks>read from <c>appsettings.json</c></remarks>
public class AuthorizationSettings
{
    /// <summary>
    /// The expected section in the <c>appsettings.json</c> file for these options is "Authorization".
    /// </summary>
    public const string SectionName = "Authorization";

    /// <summary>
    /// Default authorization options with a local authority.
    /// </summary>
    public static readonly AuthorizationSettings Default = new() 
    {
        AutoProvisionContainer = true,
        Authority = $"http://{GetLocalIpAddress()}:8080/realms/modulix",
        Audience = "modulix-api"
    };

    /// <summary>
    /// Indicates whether the container should be automatically provisioned.
    /// </summary>
    public required bool AutoProvisionContainer { get; set; }

    /// <summary>
    /// The authority URL for the OIDC provider.
    /// </summary>
    /// <value>A URL like "http://{host}/realms/{realmName}"</value>
    [Url]
    public required string Authority { get; set; }

    /// <summary>
    /// If set, the expected audience for the JWT tokens. If not set, the audience will not be validated.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Gets the local IP address of the machine. 
    /// If unable to determine, falls back to "127.0.0.1".
    /// </summary>
    /// <returns>
    /// The local IP address as a string, 
    /// or "127.0.0.1".
    /// </returns>
    private static string GetLocalIpAddress()
    {
        try
        {
            // Doesn't actually send packets; determines the preferred outbound LAN IP
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString(); // e.g., "192.168.10.69"
            }
        }
        catch
        {
            // Ignore if offline and fall back to loopback
        }

        return "127.0.0.1";
    }
}
