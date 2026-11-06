namespace Modulix.Web.Pages.Shared.Models;

public sealed record DialogHeaderModel(
    string HeadingId,
    string Title,
    string Icon,
    string? Subtitle = null);
