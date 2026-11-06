namespace Modulix.Web.Models;

/// <summary>
/// Represents an error returned by the Modulix API in an <see cref="HttpResponseMessage"/>.
/// </summary>
/// <param name="StatusCode">The HTTP status code of the response.</param>
/// <param name="Title">A short title describing the error.</param>
/// <param name="Detail">The detailed error message sent by the API server.</param>
public sealed record ApiError(int StatusCode, string Title, string Detail)
{
    /// <summary>
    /// Gets validation messages associated with individual request fields.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; init; } =
        new Dictionary<string, string[]>();

    /// <summary>
    /// Formats the status code, title, and detail into a single display message for page alerts.
    /// </summary>
    public string DisplayMessage =>
        string.IsNullOrWhiteSpace(Detail) || string.Equals(Title, Detail, StringComparison.OrdinalIgnoreCase)
            ? $"{StatusCode} {Title}"
            : $"{StatusCode} {Title}: {Detail}";
}
