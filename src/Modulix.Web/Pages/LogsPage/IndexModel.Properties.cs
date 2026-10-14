using Microsoft.AspNetCore.Mvc;

namespace Modulix.Web.Pages.LogsPage;

/// <summary>
/// The bound properties, view state and data types of the logs page.
/// </summary>
public partial class IndexModel
{
    /// <summary>
    /// The name of the service for which logs are being displayed.
    /// </summary>
    public const string ServiceName = "Modulix.Web";

    /// <summary>
    /// The service filter applied to the log files.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? Service { get; set; }

    /// <summary>
    /// The date filter applied to the log files.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? Date { get; set; }

    /// <summary>
    /// The selected log file.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "file")]
    public string? Selected { get; set; }

    /// <summary>
    /// The search query for filtering log entries.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    /// <summary>
    /// The log level filter for displaying log entries.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? Level { get; set; }

    /// <summary>
    /// Indicates whether log entries should be wrapped.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public bool Wrap { get; set; } = true;

    /// <summary>
    /// The feedback message to display to the user.
    /// </summary>
    [TempData]
    public string? Feedback { get; set; }

    /// <summary>
    /// The error feedback message to display to the user.
    /// </summary>
    [TempData]
    public string? FeedbackError { get; set; }

    /// <summary>
    /// The list of all log files in the log directory.
    /// </summary>
    public IReadOnlyList<LogFile> Files { get; private set; } = [];

    /// <summary>
    /// The list of log files that match the current filter criteria.
    /// </summary>
    public IReadOnlyList<LogFile> Matches { get; private set; } = [];

    /// <summary>
    /// The currently selected log file.
    /// </summary>
    public LogFile? SelectedFile { get; private set; }

    /// <summary>
    /// The list of log entries in the currently selected log file that match the entry filter.
    /// </summary>
    public IReadOnlyList<LogEntry> Entries { get; private set; } = [];

    /// <summary>
    /// The total number of log entries in the currently selected log file.
    /// </summary>
    public int TotalEntries { get; private set; }

    /// <summary>
    /// The number of error log entries in the currently selected log file.
    /// </summary>
    public int Errors { get; private set; }

    /// <summary>
    /// The number of warning log entries in the currently selected log file.
    /// </summary>
    public int Warnings { get; private set; }

    /// <summary>
    /// The error message encountered while listing log files, if any.
    /// </summary>
    public string? ArchiveError { get; private set; }

    /// <summary>
    /// The error message encountered while reading the selected log file, if any.
    /// </summary>
    public string? ReadError { get; private set; }

    /// <summary>
    /// Indicates whether the log entries have been successfully loaded.
    /// </summary>
    public bool IsLoaded { get; private set; }

    /// <summary>
    /// Indicates whether there is an active filter applied to the log entries.
    /// </summary>
    public bool HasEntryFilter => !string.IsNullOrWhiteSpace(Query) || !string.IsNullOrEmpty(Level);

    /// <summary>
    /// Represents a log file with its name, size, and date.
    /// </summary>
    public sealed record LogFile(string Name, long Size, DateOnly Date);

    /// <summary>
    /// Represents a log entry with its time, level, source, and message.
    /// </summary>
    public sealed record LogEntry(string Time, string Level, string Source, string Message);
}
