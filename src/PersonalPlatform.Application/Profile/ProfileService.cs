using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts.Profile;
using PersonalPlatform.Domain.Finance;
using PersonalPlatform.Domain.Users;

namespace PersonalPlatform.Application.Profile;

public interface IUserDirectory
{
    /// <summary>Email of the given user, for display in the user's own profile only.</summary>
    Task<string> GetEmailAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class ProfileService(IAppDbContext db, ICurrentUser currentUser, UserClock clock, IUserDirectory users)
{
    public async Task<ProfileDto> GetAsync(CancellationToken cancellationToken)
    {
        var context = await clock.GetAsync(cancellationToken);
        var email = await users.GetEmailAsync(currentUser.UserId, cancellationToken);
        return ToDto(context.Profile, email, context.Today);
    }

    public async Task<ProfileDto> UpdateAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var context = await clock.GetAsync(cancellationToken);
        var profile = context.Profile;
        db.SetExpectedVersion(profile, request.Version);
        profile.Update(
            request.DisplayName ?? string.Empty,
            request.TimeZoneId,
            request.Locale,
            request.CurrencyCode,
            (Domain.Users.DateFormatPreference)request.DateFormat,
            request.FirstDayOfWeek,
            request.AllowOverlappingTimeBlocks,
            (Domain.Users.ThemePreference)request.Theme,
            clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        clock.Invalidate();

        var email = await users.GetEmailAsync(currentUser.UserId, cancellationToken);
        return ToDto(profile, email, clock.TodayIn(profile.GetTimeZone()));
    }

    public static ReferenceDataDto GetReferenceData() => new(
        Currencies.SupportedCodes.Select(code => new CurrencyInfoDto(code, Currencies.MinorUnits(code))).ToList(),
        TimeZoneInfo.GetSystemTimeZones()
            .Where(z => z.HasIanaId)
            .Select(z => z.Id)
            .Order(StringComparer.Ordinal)
            .ToList(),
        UserProfile.SupportedLocales);

    private static ProfileDto ToDto(UserProfile profile, string email, DateOnly today) => new(
        email,
        profile.DisplayName,
        profile.TimeZoneId,
        profile.Locale,
        profile.CurrencyCode,
        (Contracts.Profile.DateFormatPreference)profile.DateFormat,
        profile.FirstDayOfWeek,
        profile.AllowOverlappingTimeBlocks,
        (Contracts.Profile.ThemePreference)profile.Theme,
        today,
        profile.Version);
}
