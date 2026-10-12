using System.Text;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Modulix.Web.Pages.LogsPage;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PageModel
{
    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<IndexModel> _logger;

    /// <summary>
    /// The directory where log files are stored.
    /// </summary>
    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexModel"/> class.
    /// </summary>
    /// <param name="modulixApiClient">The Modulix API client.</param>
    /// <param name="logger">The logger instance.</param>
    public IndexModel(
        IWebHostEnvironment env,
        ILogger<IndexModel> logger)
    {
        _logger = logger;
        _directory = Path.Combine(env.ContentRootPath, "logs");
    }

    private static readonly Regex LogName =
        new(@"\Alog-[0-9]{8}(?:_[0-9]{3})?\.log\z");

    /// <summary>
    /// The regular expression used to validate log file names.
    /// </summary>
    public IActionResult OnGetFiles()
    {
        if (!Directory.Exists(_directory))
            return new JsonResult(Array.Empty<object>());

        var files = new DirectoryInfo(_directory).EnumerateFiles("log-*.log")
            .Where(file => LogName.IsMatch(file.Name)
                && (file.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new { file.Name, Size = file.Length });

        return new JsonResult(files.ToArray());
    }

    /// <summary>
    /// Reads the contents of a specified log file.
    /// </summary>
    /// <param name="name">The name of the log file to read.</param>
    public async Task<IActionResult> OnGetReadAsync(string? name)
    {
        if (name is null || !LogName.IsMatch(name))
            return BadRequest();

        try
        {
            var file = new FileInfo(Path.Combine(_directory, name));
            if (!file.Exists) return NotFound();
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                return BadRequest();

            await using var stream = new FileStream(file.FullName,
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[1024 * 1024 + 1];
            var count = await stream.ReadAtLeastAsync(buffer, buffer.Length,
                false, HttpContext.RequestAborted);

            if (count == buffer.Length) return StatusCode(413);
            return Content(Encoding.UTF8.GetString(buffer, 0, count),
                "text/plain; charset=utf-8");
        }
        catch (IOException) { return StatusCode(503); }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
    }
}