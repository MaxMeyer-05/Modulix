using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Modulix.Web.Models;

namespace Modulix.Web.Extensions;

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
        IReadOnlyDictionary<string, string[]> fieldErrors = new Dictionary<string, string[]>();

        var content = await response.Content.ReadAsStringAsync(ct);
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                var problem = JsonSerializer.Deserialize<ValidationProblemDetails>(content, JsonSerializerOptions.Web);
                if (problem is not null)
                {
                    if (problem.Errors is { Count: > 0 })
                    {
                        var messagesByField = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var (field, messages) in problem.Errors)
                        {
                            if (messages is null || messages.Length == 0 || messages.Any(string.IsNullOrWhiteSpace))
                                throw new JsonException("The API returned an invalid field validation message.");

                            if (!messagesByField.TryGetValue(field, out var existingMessages))
                            {
                                existingMessages = [];
                                messagesByField[field] = existingMessages;
                            }
                            existingMessages.AddRange(messages);
                        }

                        fieldErrors = messagesByField.ToDictionary(
                            entry => entry.Key,
                            entry => entry.Value.ToArray(),
                            StringComparer.OrdinalIgnoreCase);
                    }

                    if (!string.IsNullOrWhiteSpace(problem.Title))
                        title = problem.Title;

                    if (!string.IsNullOrWhiteSpace(problem.Detail))
                    {
                        detail = problem.Detail;
                    }
                    else if (fieldErrors.Count > 0)
                    {
                        detail = string.Join(" ", fieldErrors.Values.SelectMany(messages => messages));
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

        return new ApiError(statusCode, title, detail) { FieldErrors = fieldErrors };
    }
}
