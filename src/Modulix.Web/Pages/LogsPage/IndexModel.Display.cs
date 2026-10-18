using System.Globalization;

using Microsoft.AspNetCore.Routing;

namespace Modulix.Web.Pages.LogsPage;

/// <summary>
/// The formatting and link helpers used by the logs view.
/// </summary>
public partial class IndexModel
{
    /// <summary>
    /// Gets the current date.
    /// </summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>
    /// Formats the specified date as a string.
    /// </summary>
    /// <param name="date">The date to format.</param>
    /// <returns>The formatted date string.</returns>
    public static string FormatDate(DateOnly date) =>
        date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats the specified file size as a human-readable string.
    /// </summary>
    /// <param name="bytes">The file size in bytes.</param>
    /// <returns>The formatted file size string.</returns>
    public static string FormatSize(long bytes) =>
        $"{(bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture)} KB";

    /// <summary>
    /// Returns the pluralized form of a word based on the specified count.
    /// </summary>
    /// <param name="count">The count of items.</param>
    /// <param name="singular">The singular form of the word.</param>
    /// <returns>The pluralized form of the word if count is not 1; otherwise, the singular form.</returns>
    public static string Plural(int count, string singular) =>
        count == 1 ? singular : singular + "s";

    /// <summary>
    /// Gets the CSS class for the specified log level badge.
    /// </summary>
    /// <param name="level">The log level.</param>
    /// <returns>The CSS class for the badge.</returns>
    public static string BadgeClass(string level) => level switch
    {
        "FATAL" => "error",
        "TRACE" => "debug",
        _ => level.ToLowerInvariant()
    };

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
}
