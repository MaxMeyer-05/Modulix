using Server.Models;

namespace Server.Security;

/// <summary>
/// Represents the result of a token validation operation.
/// </summary>
public class TokenValidationResult
{
    /// <summary>
    /// Indicates whether the token is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Defines the error message if the token is invalid.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Contains the user session data if the token is valid.
    /// </summary>
    public UserSessionDataDto? UserSessionData { get; init; }

    /// <summary>
    /// Creates a successful token validation result with the specified user session data.
    /// </summary>
    public static TokenValidationResult Success(UserSessionDataDto sessionData) =>
        new() { IsValid = true, UserSessionData = sessionData };

    /// <summary>
    /// Creates a failed token validation result with the specified error message.
    /// </summary>
    public static TokenValidationResult Failure(string errorMessage) =>
        new() { IsValid = false, ErrorMessage = errorMessage };
}