using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Modulix.Web.Pages.LogsPage;

/// <summary>
/// The model for the logs page. The handlers are defined here; the properties, display helpers,
/// file access and parsing live in the <c>IndexModel.*.cs</c> partial files.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public partial class IndexModel : PageModel
{
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

    /// <summary>
    /// Handles the GET request for the page and loads the log entries.
    /// </summary>
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

    /// <summary>
    /// Handles the POST request to delete a log file.
    /// </summary>
    /// <param name="name">The name of the log file to delete.</param>
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
}
