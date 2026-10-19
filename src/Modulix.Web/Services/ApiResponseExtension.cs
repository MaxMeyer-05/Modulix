using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Modulix.Web.Services;

/// <summary>
/// Represents an error returned by the Modulix API in an <see cref="HttpResponseMessage"/>.
/// </summary>
/// <param name="StatusCode">The HTTP status code of the response.</param>
/// <param name="Title">A short title describing the error.</param>
/// <param name="Detail">The detailed error message sent by the API server.</param>
public sealed record ApiError(int StatusCode, string Title, string Detail)
{
    /// <summary>
    /// Formats the status code, title, and detail into a single display message for page alerts.
    /// </summary>
    public string DisplayMessage =>
        string.IsNullOrWhiteSpace(Detail) || string.Equals(Title, Detail, StringComparison.OrdinalIgnoreCase)
            ? $"{StatusCode} {Title}"
            : $"{StatusCode} {Title}: {Detail}";
}

/// <summary>
/// Provides helper methods for reading error details from <see cref="HttpResponseMessage"/> instances.
/// </summary>
public static class ApiResponseExtension
{
    /// <summary>
    /// Extracts the status code, title, and detailed server message from a non-success <see cref="HttpResponseMessage"/>.
    /// </summary>
    /// <param name="response">The HTTP response message returned by the API client.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>An <see cref="ApiError"/> containing the server-provided error details.</returns>
    public static async Task<ApiError> ReadApiErrorAsync(this HttpResponseMessage response, CancellationToken ct = default)
    {
        var statusCode = (int)response.StatusCode;
        var defaultTitle = !string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? response.ReasonPhrase
            : ReasonPhrases.GetReasonPhrase(statusCode);

        if (string.IsNullOrWhiteSpace(defaultTitle))
            defaultTitle = $"HTTP {statusCode}";

        var title = defaultTitle;
        var detail = defaultTitle;

        var content = await response.Content.ReadAsStringAsync(ct);
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                var problem = JsonSerializer.Deserialize<ValidationProblemDetails>(content, JsonSerializerOptions.Web);
                if (problem is not null)
                {
                    if (!string.IsNullOrWhiteSpace(problem.Title))
                        title = problem.Title;

                    if (!string.IsNullOrWhiteSpace(problem.Detail))
                    {
                        detail = problem.Detail;
                    }
                    else if (problem.Errors.Count > 0)
                    {
                        detail = string.Join(" ", problem.Errors.Values.SelectMany(messages => messages));
                    }
                    else if (!string.IsNullOrWhiteSpace(problem.Title))
                    {
                        detail = problem.Title;
                    }
                }
            }
            catch (JsonException)
            {
                detail = content.Trim();
            }
        }

        return new ApiError(statusCode, title, detail);
    }
}
