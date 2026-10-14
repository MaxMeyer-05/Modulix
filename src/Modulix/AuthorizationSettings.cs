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
    public static readonly AuthorizationSettings Default = new() { Authority = "http://localhost" };

    /// <summary>
    /// The authority URL for the OIDC provider.
    /// </summary>
    /// <value>A URL like "http://{host}/auth/realms/{realmName}"</value>
    [Url]
    public required string Authority { get; set; }

    /// <summary>
    /// If set, the expected audience for the JWT tokens. If not set, the audience will not be validated.
    /// </summary>
    public string? Audience { get; set; }
}
