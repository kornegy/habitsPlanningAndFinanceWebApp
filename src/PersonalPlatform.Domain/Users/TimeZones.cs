namespace PersonalPlatform.Domain.Users;

/// <summary>
/// Resolves IANA time zone ids. Only IANA ids are accepted (Windows ids are rejected) so that stored
/// values are portable between hosts.
/// </summary>
public static class TimeZones
{
    public const int MaxIdLength = 64;

    public static bool IsSupported(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaxIdLength)
        {
            return false;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            return false;
        }

        // Accept only canonical IANA ids, not Windows display ids that some platforms map.
        return zone.HasIanaId || string.Equals(id, "UTC", StringComparison.Ordinal);
    }

    public static TimeZoneInfo Find(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>The user's current local calendar date.</summary>
    public static DateOnly Today(TimeProvider clock, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);

    /// <summary>
    /// Converts the start of a local calendar day to an absolute instant. Handles DST gaps: if local
    /// midnight does not exist, the first valid instant of that day is used.
    /// </summary>
    public static DateTimeOffset StartOfDayUtc(DateOnly date, TimeZoneInfo zone) => ToUtc(date, TimeOnly.MinValue, zone);

    /// <summary>
    /// Converts a local date + time to an absolute instant. A time inside a DST gap is moved forward to the
    /// first valid local time; an ambiguous time (DST fall-back) resolves to its earlier instant.
    /// </summary>
    public static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(15);
        }

        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
