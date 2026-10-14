using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Modulix.Web.Pages.LogsPage;

/// <summary>
/// The file system access of the logs page.
/// </summary>
public partial class IndexModel
{
    /// <summary>
    /// The maximum number of bytes to read from a log file.
    /// </summary>
    private const int MaxReadBytes = 1024 * 1024;

    /// <summary>
    /// The regular expression used to match log file names.
    /// </summary>
    private static readonly Regex LogName = LogNameRegex();

    /// <summary>
    /// Loads the log files from the directory and applies the current filters.
    /// </summary>
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

    /// <summary>
    /// Tries to create a <see cref="LogFile"/> instance from the specified file info.
    /// </summary>
    /// <param name="file">The file info.</param>
    /// <returns>The created <see cref="LogFile"/> instance, or <c>null</c> if the file name is invalid.</returns>
    private static LogFile? TryCreate(FileInfo file)
    {
        var match = LogName.Match(file.Name);
        if (!match.Success) return null;
        if (!DateOnly.TryParseExact(match.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)) return null;

        return new LogFile(file.Name, file.Length, date);
    }

    /// <summary>
    /// Resolves the specified log file name to a <see cref="FileInfo"/> instance.
    /// </summary>
    /// <param name="name">The name of the log file.</param>
    /// <param name="status">The status code indicating the result of the resolution.</param>
    /// <returns>The resolved <see cref="FileInfo"/> instance, or <c>null</c> if the file could not be resolved.</returns>
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

    /// <summary>
    /// Opens the specified log file for reading with shared read and delete access.
    /// </summary>
    /// <param name="file">The log file to open.</param>
    /// <returns>A <see cref="FileStream"/> for reading the log file.</returns>
    private static FileStream OpenRead(FileInfo file) =>
        new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// Reads the contents of the specified log file asynchronously.
    /// </summary>
    /// <param name="name">The name of the log file to read.</param>
    /// <returns>A tuple containing the text of the log file and any error message.</returns>
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

    /// <summary>
    /// Gets the regular expression for matching log file names.
    /// </summary>
    [GeneratedRegex(@"\Alog-(?<date>[0-9]{8})(?:_[0-9]{3})?\.log\z")]
    private static partial Regex LogNameRegex();
}
