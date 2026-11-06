using Microsoft.AspNetCore.Mvc.ModelBinding;
using Modulix.Web.Models;

namespace Modulix.Web.Helpers;

/// <summary>
/// Preserves field-level validation messages when a dialog is reopened.
/// </summary>
public static class FormValidationErrors
{
    /// <summary>
    /// Collects model binding and validation errors by field name.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> FromModelState(ModelStateDictionary modelState)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, entry) in modelState)
        {
            if (string.IsNullOrEmpty(key) || entry is null || entry.Errors.Count == 0)
                continue;

            result[key] = entry.Errors
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "A form field has an invalid value."
                    : error.ErrorMessage)
                .ToArray();
        }

        return result;
    }

    /// <summary>
    /// Creates a field-level error for a validation rule outside model binding.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> ForField(string field, string message) =>
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [field] = [message] };

    /// <summary>
    /// Includes API validation messages in a form's error summary.
    /// </summary>
    public static string Describe(ApiError error)
    {
        var details = new List<string> { error.Detail };
        foreach (var message in error.FieldErrors.Values.SelectMany(messages => messages))
        {
            if (!string.IsNullOrWhiteSpace(message)
                && !details.Any(detail => detail.Contains(message, StringComparison.OrdinalIgnoreCase)))
            {
                details.Add(message);
            }
        }

        return string.Join(" ", details);
    }
}
