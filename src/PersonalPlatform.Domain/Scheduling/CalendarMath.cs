namespace PersonalPlatform.Domain.Scheduling;

/// <summary>Pure calendar helpers operating on local calendar dates (no time zone involved).</summary>
public static class CalendarMath
{
    /// <summary>Monday of the ISO week containing <paramref name="date"/>.</summary>
    public static DateOnly StartOfIsoWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0 ... Sunday = 6
        return date.AddDays(-offset);
    }

    public static DateOnly StartOfWeek(DateOnly date, DayOfWeek firstDay)
    {
        var offset = ((int)date.DayOfWeek - (int)firstDay + 7) % 7;
        return date.AddDays(-offset);
    }

    public static DateOnly StartOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    public static DateOnly EndOfMonth(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    public static int MonthsBetween(DateOnly from, DateOnly to) =>
        ((to.Year - from.Year) * 12) + to.Month - from.Month;

    /// <summary>
    /// Builds a date in the given month, clamping the day to the month length
    /// (e.g. day 31 in April becomes April 30, Feb 29 in a common year becomes Feb 28).
    /// </summary>
    public static DateOnly ClampedDate(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}
