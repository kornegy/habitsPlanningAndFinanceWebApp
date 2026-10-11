using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalPlatform.Infrastructure.Persistence;

namespace PersonalPlatform.Infrastructure.Identity;

/// <summary>
/// Keeps session tables bounded: deletes refresh tokens that expired more than a day ago (they can no longer
/// be exchanged, so reuse detection does not need them) and sessions that ended longer than the retention period
/// ago. Runs hourly; safe to run on several instances concurrently (plain set-based DELETEs).
/// </summary>
public sealed partial class SessionCleanupService(
    IServiceScopeFactory scopes,
    IOptions<SessionOptions> options,
    TimeProvider time,
    ILogger<SessionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is DbUpdateException or Npgsql.NpgsqlException or InvalidOperationException or TimeoutException)
            {
                LogCleanupFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task CleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = time.GetUtcNow();
        var tokenCutoff = now.AddDays(-1);
        var sessionCutoff = now.AddDays(-options.Value.RetentionDays);

        var tokens = await db.RefreshTokens.Where(t => t.ExpiresAt < tokenCutoff).ExecuteDeleteAsync(cancellationToken);
        var sessions = await db.UserSessions
            .Where(s => s.ExpiresAt < sessionCutoff || (s.RevokedAt != null && s.RevokedAt < sessionCutoff))
            .ExecuteDeleteAsync(cancellationToken);
        if (tokens + sessions > 0)
        {
            LogCleanup(logger, tokens, sessions);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Session cleanup removed {Tokens} refresh tokens and {Sessions} sessions")]
    private static partial void LogCleanup(ILogger logger, int tokens, int sessions);

    [LoggerMessage(Level = LogLevel.Error, Message = "Session cleanup failed")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
