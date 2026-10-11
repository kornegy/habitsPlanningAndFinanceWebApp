using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Contracts.Admin;
using PersonalPlatform.Infrastructure.Persistence;

namespace PersonalPlatform.Infrastructure.Identity;

/// <summary>Aggregate counters for operators. Returns no personal data.</summary>
public sealed class AdminStatsService(AppDbContext db, TimeProvider time)
{
    public async Task<AdminStatsDto> GetAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var users = await db.Users.CountAsync(cancellationToken);
        var confirmed = await db.Users.CountAsync(u => u.EmailConfirmed, cancellationToken);
        var sessions = await db.UserSessions.CountAsync(s => s.RevokedAt == null && s.ExpiresAt > now, cancellationToken);
        return new AdminStatsDto(users, confirmed, sessions, now);
    }
}
