using System.Text.RegularExpressions;

namespace Modulix.Web.Pages.LogsPage;

/// <summary>
/// The log entry parsing of the logs page.
/// </summary>
public partial class IndexModel
{
    /// <summary>
    /// The regular expression used to match log entry headers.
    /// </summary>
    private static readonly Regex EntryHeader = EntryHeaderRegex();

    /// <summary>
    /// The mapping of log level abbreviations to their full names.
    /// </summary>
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
    /// Parses the log entries from the specified text.
    /// </summary>
    /// <param name="text">The text containing the log entries.</param>
    /// <returns>A list of parsed <see cref="LogEntry"/> instances.</returns>
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

    /// <summary>
    /// Gets the regular expression for matching log entry headers.
    /// </summary>
    [GeneratedRegex(@"^\[\d{4}-\d{2}-\d{2} (?<time>\d{2}:\d{2}:\d{2}(?:\.\d+)?) [^|]*\|\[(?<level>[A-Z]{3})\]\|(?<extra>.*?)\|(?<source>[^|]*)\|(?<message>.*)\|$")]
    private static partial Regex EntryHeaderRegex();
}
