using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Infrastructure.Persistence;

namespace PersonalPlatform.Infrastructure.Identity;

/// <summary>
/// Per-request check that the session referenced by an access token is still active. This makes logout,
/// "sign out everywhere", password changes and refresh-token reuse detection effective immediately rather than
/// after the access token expires. Cost: one primary-key lookup per authenticated request.
/// </summary>
public sealed class SessionValidator(AppDbContext db, TimeProvider time)
{
    public Task<bool> IsActiveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return db.UserSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now, cancellationToken);
    }
}
