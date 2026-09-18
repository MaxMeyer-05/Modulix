using System.Text;
using Microsoft.Extensions.Options;

namespace Server.Security;

/// <summary>
/// Validates JWT settings when the application starts.
/// </summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("JWT issuer must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("JWT audience must be configured.");
        }

        if (options.AccessTokenLifetimeMinutes <= 0)
        {
            failures.Add("JWT access token lifetime must be a positive integer.");
        }

        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            failures.Add("JWT secret key must be configured.");
        }
        else if (Encoding.UTF8.GetByteCount(options.SecretKey) < 32)
        {
            failures.Add("JWT secret key must be at least 32 bytes long.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}