using System.Diagnostics;
using PersonalPlatform.Contracts;

namespace PersonalPlatform.Api.Infrastructure;

/// <summary>
/// Assigns every request a correlation id (the W3C trace id), adds it to the log scope and returns it in a
/// response header so that a user-reported error can be matched to server logs. Client-supplied ids are
/// ignored to prevent log injection and spoofing.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[ApiHeaders.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}
