using Microsoft.AspNetCore.Diagnostics;

namespace Server.Infrastructure;

/// <summary>
/// Converts expected application exceptions into RFC 7807 problem details responses.
/// </summary>
public sealed class GlobalExceptionHandler: IExceptionHandler
{
    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<GlobalExceptionHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalExceptionHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, detail) = exception switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request.", exception.Message),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized.", exception.Message),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found.", exception.Message),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Operation conflict.", exception.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "An unexpected error occurred.")
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.", httpContext.Request.Method, httpContext.Request.Path);
        else
            _logger.LogWarning(exception, "Request failed with status code {StatusCode}.", statusCode);

        await Results.Problem(statusCode: statusCode, title: title, detail: detail)
            .ExecuteAsync(httpContext);
        return true;
    }
}