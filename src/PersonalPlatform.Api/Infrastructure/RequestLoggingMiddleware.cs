using System.Diagnostics;

namespace PersonalPlatform.Api.Infrastructure;

/// <summary>
/// One structured log line per API request: method, route template, status and duration. Query strings,
/// headers, cookies and bodies are never logged (they may contain search terms, tokens or personal data).
/// </summary>
public sealed partial class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            var status = context.Response.StatusCode;
            if (status >= 500)
            {
                LogFailed(logger, context.Request.Method, route, status, elapsed);
            }
            else
            {
                LogCompleted(logger, context.Request.Method, route, status, elapsed);
            }
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs:0.0} ms")]
    private static partial void LogCompleted(ILogger logger, string method, string route, int statusCode, double elapsedMs);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs:0.0} ms")]
    private static partial void LogFailed(ILogger logger, string method, string route, int statusCode, double elapsedMs);
}
