namespace Modulix.Web.Pages;

public sealed record PageEmptyStateModel(string Title, string Icon)
{
    public string? Description { get; init; }
    public string? Id { get; init; }
    public string? TitleId { get; init; }
    public string? ActionText { get; init; }
    public string? ActionUrl { get; init; }
    public string? ActionIcon { get; init; }
    public string? Hint { get; init; }
    public bool InSection { get; init; }
    public bool PrimaryAction { get; init; }
}
