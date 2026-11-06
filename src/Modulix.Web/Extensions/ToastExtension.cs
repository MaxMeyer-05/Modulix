using System.Text.Json;

using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Modulix.Web.Models;

namespace Modulix.Web.Extensions;

/// <summary>
/// Stores toast notifications in <see cref="ITempDataDictionary"/> so they survive a redirect
/// and are rendered once by the shared layout.
/// </summary>
public static class ToastExtension
{
    /// <summary>
    /// The TempData key that holds pending toast notifications.
    /// </summary>
    public const string TempDataKey = "Modulix.Toasts";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Queues a toast notification for the next rendered page.
    /// </summary>
    public static void AddToast(this ITempDataDictionary tempData, ToastMessage toast)
    {
        var toasts = Read(tempData.Peek(TempDataKey));
        toasts.Add(toast);
        tempData[TempDataKey] = JsonSerializer.Serialize(toasts, JsonOptions);
    }

    /// <summary>
    /// Returns and removes all queued toast notifications.
    /// </summary>
    public static IReadOnlyList<ToastMessage> TakeToasts(this ITempDataDictionary tempData) =>
        Read(tempData[TempDataKey]);

    private static List<ToastMessage> Read(object? value)
    {
        if (value is not string json || string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<ToastMessage>>(json, JsonOptions)?
                .Where(toast => !string.IsNullOrWhiteSpace(toast.Title))
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
