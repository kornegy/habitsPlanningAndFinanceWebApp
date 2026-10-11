using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Domain.Users;

namespace PersonalPlatform.Application.Common;

/// <param name="Today">The owner's current local calendar date in their configured time zone.</param>
public sealed record UserTimeContext(UserProfile Profile, TimeZoneInfo Zone, DateTimeOffset Now, DateOnly Today);

/// <summary>
/// Resolves "now" and "today" for the current user. Calendar logic (due today, habit dates, monthly reports)
/// always uses the user's time zone, never the server's local time. Scoped per request.
/// </summary>
public sealed class UserClock(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    private UserTimeContext? _cached;

    public DateTimeOffset UtcNow => time.GetUtcNow();

    public DateOnly TodayIn(TimeZoneInfo zone) => TimeZones.Today(time, zone);

    /// <summary>Forget the cached context, e.g. after the profile's time zone changed.</summary>
    public void Invalidate() => _cached = null;

    public async Task<UserTimeContext> GetAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var userId = currentUser.UserId;
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.OwnerId == userId, cancellationToken)
            ?? throw new NotFoundException("Profile not found.");
        var zone = profile.GetTimeZone();
        var now = time.GetUtcNow();
        _cached = new UserTimeContext(profile, zone, now, TimeZones.Today(time, zone));
        return _cached;
    }
}
