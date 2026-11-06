namespace Modulix.Web.Models;

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
