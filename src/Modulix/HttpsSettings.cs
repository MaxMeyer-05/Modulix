using System.ComponentModel.DataAnnotations;

namespace Modulix;

/// <summary>
/// Settings for HTTPS configuration.
/// </summary>
/// <remarks>read from <c>appsettings.json</c></remarks>
public class HttpsSettings
{
    /// <summary>
    /// The expected section in the <c>appsettings.json</c> file for these options is "Https".
    /// </summary>
    public const string SectionName = "Https";

    /// <summary>
    /// Default HTTPS options with HTTPS disabled.
    /// </summary>
    public static readonly HttpsSettings Default = new() { Enabled = false };

    /// <summary>
    /// Indicates whether HTTPS redirection and HSTS are enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// If set, the HTTPS port used for redirecting HTTP requests.
    /// </summary>
    [Range(1, 65535)]
    public int? Port { get; set; }

    /// <summary>
    /// If set, the path to the PKCS#12 (<c>.pfx</c>) certificate file for Kestrel.
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// If set, the password for the certificate specified by <see cref="CertificatePath"/>.
    /// </summary>
    public string? CertificatePassword { get; set; }
}
