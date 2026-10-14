using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;

namespace Modulix.Web.Pages.LogsPage;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public partial class IndexModel : PageModel
{
    public const string ServiceName = "Modulix.Web";

    private const int MaxReadBytes = 1024 * 1024;

    private static readonly Regex LogName = LogNameRegex();

    private static readonly Regex EntryHeader = EntryHeaderRegex();

    private static readonly Dictionary<string, string> Levels = new()
    {
        ["VRB"] = "TRACE",
        ["DBG"] = "DEBUG",
        ["INF"] = "INFO",
        ["WRN"] = "WARN",
        ["ERR"] = "ERROR",
        ["FTL"] = "FATAL"
    };

    /// <summary>
    /// The directory where log files are stored.
    /// </summary>
    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexModel"/> class.
    /// </summary>
    /// <param name="env">The web host environment.</param>
    public IndexModel(IWebHostEnvironment env)
    {
        _directory = Path.Combine(env.ContentRootPath, "logs");
    }

    [BindProperty(SupportsGet = true)]
    public string? Service { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Date { get; set; }

    [BindProperty(SupportsGet = true, Name = "file")]
    public string? Selected { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Level { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Wrap { get; set; } = true;

    [TempData]
    public string? Feedback { get; set; }

    [TempData]
    public string? FeedbackError { get; set; }

    public IReadOnlyList<LogFile> Files { get; private set; } = [];

    public IReadOnlyList<LogFile> Matches { get; private set; } = [];

    public LogFile? SelectedFile { get; private set; }

    public IReadOnlyList<LogEntry> Entries { get; private set; } = [];

    public int TotalEntries { get; private set; }

    public int Errors { get; private set; }

    public int Warnings { get; private set; }

    public string? ArchiveError { get; private set; }

    public string? ReadError { get; private set; }

    public bool IsLoaded { get; private set; }

    public bool HasEntryFilter => !string.IsNullOrWhiteSpace(Query) || !string.IsNullOrEmpty(Level);

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public static string FormatDate(DateOnly date) =>
        date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string FormatSize(long bytes) =>
        $"{(bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture)} KB";

    public static string Plural(int count, string singular) =>
        count == 1 ? singular : singular + "s";

    public static string BadgeClass(string level) => level switch
    {
        "FATAL" => "error",
        "TRACE" => "debug",
        _ => level.ToLowerInvariant()
    };

    public async Task OnGetAsync()
    {
        LoadFiles();

        SelectedFile = Matches.FirstOrDefault(file => file.Name == Selected) ?? Matches.FirstOrDefault();
        if (SelectedFile is null) return;

        var (text, error) = await ReadAsync(SelectedFile.Name);
        ReadError = error;
        if (text is null) return;

        IsLoaded = true;
        var all = ParseEntries(text);
        TotalEntries = all.Count;
        Errors = all.Count(entry => entry.Level is "ERROR" or "FATAL");
        Warnings = all.Count(entry => entry.Level == "WARN");

        var query = Query?.Trim() ?? "";
        Entries = all.Where(entry =>
                (string.IsNullOrEmpty(Level) || entry.Level == Level)
                && $"{entry.Time} {entry.Level} {entry.Source} {entry.Message}"
                    .Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>
    /// Streams the specified log file as a download.
    /// </summary>
    /// <param name="name">The name of the log file.</param>
    public IActionResult OnGetDownload(string? name)
    {
        try
        {
            var file = Resolve(name, out var status);
            if (file is null) return StatusCode(status);

            return File(OpenRead(file), "text/plain; charset=utf-8", file.Name);
        }
        catch (IOException) { return StatusCode(503); }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
    }

    public IActionResult OnPostDelete(string? name)
    {
        try
        {
            var file = Resolve(name, out var status);
            if (file is null)
            {
                if (status == 404) Feedback = $"{name} no longer exists.";
                else FeedbackError = "Could not delete the file. Reload the page and try again.";
            }
            else
            {
                file.Delete();
                Feedback = $"{file.Name} deleted.";
            }
        }
        catch (IOException)
        {
            FeedbackError = "The log file is in use or temporarily unavailable. Try again later.";
        }
        catch (UnauthorizedAccessException)
        {
            FeedbackError = "You do not have permission to delete this log file.";
        }

        return RedirectToPage(PageValues(null));
    }

    /// <summary>
    /// Builds the route values that preserve the current filters.
    /// </summary>
    /// <param name="file">The file to select, or <c>null</c> to select the first match.</param>
    /// <param name="includeEntryFilter">Whether the search and level filter are kept.</param>
    /// <param name="includeArchiveFilter">Whether the application and date filter are kept.</param>
    public RouteValueDictionary PageValues(string? file, bool includeEntryFilter = true, bool includeArchiveFilter = true)
    {
        var values = new RouteValueDictionary();
        if (file is not null) values["file"] = file;
        if (includeArchiveFilter)
        {
            if (!string.IsNullOrEmpty(Service)) values["service"] = Service;
            if (Date is { } date) values["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        if (includeEntryFilter)
        {
            if (!string.IsNullOrWhiteSpace(Query)) values["q"] = Query;
            if (!string.IsNullOrEmpty(Level)) values["level"] = Level;
        }
        if (!Wrap) values["wrap"] = "false";
        return values;
    }

    private void LoadFiles()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Files = new DirectoryInfo(_directory).EnumerateFiles("log-*.log")
                    .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
                    .Select(TryCreate)
                    .OfType<LogFile>()
                    .OrderByDescending(file => file.Name, StringComparer.Ordinal)
                    .ToArray();
            }
        }
        catch (IOException) { ArchiveError = "The log files are temporarily unavailable."; }
        catch (UnauthorizedAccessException) { ArchiveError = "You do not have permission to view these logs."; }

        Matches = Files
            .Where(file => (string.IsNullOrEmpty(Service) || Service == ServiceName)
                && (Date is null || file.Date == Date))
            .ToArray();
    }

    private static LogFile? TryCreate(FileInfo file)
    {
        var match = LogName.Match(file.Name);
        if (!match.Success) return null;
        if (!DateOnly.TryParseExact(match.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)) return null;

        return new LogFile(file.Name, file.Length, date);
    }

    private FileInfo? Resolve(string? name, out int status)
    {
        status = 400;
        if (name is null || !LogName.IsMatch(name)) return null;

        var file = new FileInfo(Path.Combine(_directory, name));
        if (!file.Exists)
        {
            status = 404;
            return null;
        }

        return (file.Attributes & FileAttributes.ReparsePoint) != 0 ? null : file;
    }

    private static FileStream OpenRead(FileInfo file) =>
        new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private async Task<(string? Text, string? Error)> ReadAsync(string name)
    {
        try
        {
            var file = Resolve(name, out var status);
            if (file is null)
            {
                return (null, status == 404
                    ? "This log file no longer exists."
                    : "This log file could not be opened.");
            }

            await using var stream = OpenRead(file);
            var buffer = new byte[MaxReadBytes + 1];
            var count = await stream.ReadAtLeastAsync(buffer, buffer.Length, false, HttpContext.RequestAborted);

            return count == buffer.Length
                ? (null, "This log file exceeds the 1 MiB read limit.")
                : (Encoding.UTF8.GetString(buffer, 0, count), null);
        }
        catch (IOException) { return (null, "The log file is temporarily unavailable."); }
        catch (UnauthorizedAccessException) { return (null, "You do not have permission to view these logs."); }
    }

    private static List<LogEntry> ParseEntries(string text)
    {
        var entries = new List<LogEntry>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var match = EntryHeader.Match(line);
            if (match.Success)
            {
                var extra = match.Groups["extra"].Value;
                var message = match.Groups["message"].Value;
                entries.Add(new LogEntry(
                    match.Groups["time"].Value,
                    Levels.GetValueOrDefault(match.Groups["level"].Value, "UNKNOWN"),
                    match.Groups["source"].Value,
                    extra.Length > 0 ? $"{message}\n{extra}" : message));
            }
            else if (entries.Count > 0)
            {
                entries[^1] = entries[^1] with { Message = $"{entries[^1].Message}\n{line}" };
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                entries.Add(new LogEntry("", "UNKNOWN", "", line));
            }
        }

        return entries;
    }

    [GeneratedRegex(@"\Alog-(?<date>[0-9]{8})(?:_[0-9]{3})?\.log\z")]
    private static partial Regex LogNameRegex();

    [GeneratedRegex(@"^\[\d{4}-\d{2}-\d{2} (?<time>\d{2}:\d{2}:\d{2}(?:\.\d+)?) [^|]*\|\[(?<level>[A-Z]{3})\]\|(?<extra>.*?)\|(?<source>[^|]*)\|(?<message>.*)\|$")]
    private static partial Regex EntryHeaderRegex();

    public sealed record LogFile(string Name, long Size, DateOnly Date);

    public sealed record LogEntry(string Time, string Level, string Source, string Message);
}
