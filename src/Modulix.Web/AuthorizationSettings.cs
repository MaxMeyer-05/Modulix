using System.ComponentModel.DataAnnotations;

namespace Modulix.Web;

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
    /// The authority URL for the OIDC provider.
    /// </summary>
    /// <value>A URL like "http://{host}/realms/{realmName}"</value>
    [Url]
    public required string Authority { get; set; }
    
    /// <summary>
    /// The client (<c>modulix_Service</c>) used to call the APIs via client credentials.
    /// </summary>
    public required ClientSettings Service { get; set; }

    /// <summary>
    /// The client (<c>modulix_Client</c>) used for the interactive user login (OpenID Connect).
    /// </summary>
    public required ClientSettings Client { get; set; }
}

/// <summary>
/// Credentials of an OIDC client.
/// </summary>
public class ClientSettings
{
    /// <summary>
    /// The client ID of the OIDC client.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// The client secret of the OIDC client.
    /// </summary>
    public required string ClientSecret { get; set; }
}
