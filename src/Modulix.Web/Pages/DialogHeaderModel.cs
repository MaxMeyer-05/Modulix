namespace Modulix.Web.Pages;

public sealed record DialogHeaderModel(
    string HeadingId,
    string Title,
    string Icon,
    string? Subtitle = null);
