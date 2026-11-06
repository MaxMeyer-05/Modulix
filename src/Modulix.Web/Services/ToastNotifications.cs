using System.Text.Json;

using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Modulix.Web.Services;

/// <summary>
/// The visual style of a toast notification.
/// </summary>
public enum ToastVariant
{
    /// <summary>
    /// A completed action.
    /// </summary>
    Success,

    /// <summary>
    /// Neutral information, such as an action that continues in the background.
    /// </summary>
    Info
}

/// <summary>
/// A short, non-blocking notification shown after a redirect.
/// </summary>
/// <param name="Title">The short title of the notification.</param>
/// <param name="Description">Optional additional text.</param>
/// <param name="Variant">The visual style.</param>
/// <param name="LinkUrl">Optional URL for a follow-up action.</param>
/// <param name="LinkText">The text of the follow-up action link.</param>
public sealed record ToastMessage(
    string Title,
    string? Description = null,
    ToastVariant Variant = ToastVariant.Success,
    string? LinkUrl = null,
    string? LinkText = null);

/// <summary>
/// Stores toast notifications in <see cref="ITempDataDictionary"/> so they survive a redirect
/// and are rendered once by the shared layout.
/// </summary>
public static class ToastNotifications
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
