using Microsoft.AspNetCore.Mvc.Rendering;

namespace Modulix.Web.Pages;

public sealed record PageErrorStateModel(string Title)
{
    public string Icon { get; init; } = "circle-alert";
    public string? Description { get; init; }
    public string? Id { get; init; }
    public string? TitleId { get; init; }
    public string? DescriptionId { get; init; }
    public string? ActionText { get; init; }
    public string? ActionUrl { get; init; }
    public ValidationSummary ValidationSummary { get; init; } = ValidationSummary.None;
}
