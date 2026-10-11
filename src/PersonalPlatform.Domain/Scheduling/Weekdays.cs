namespace PersonalPlatform.Domain.Scheduling;

/// <summary>Set of days of the week, persisted as a small integer bit mask.</summary>
[Flags]
#pragma warning disable CA1711 // "Days" is the natural name of this flags set.
public enum Weekdays
#pragma warning restore CA1711
{
    None = 0,
    Monday = 1 << 0,
    Tuesday = 1 << 1,
    Wednesday = 1 << 2,
    Thursday = 1 << 3,
    Friday = 1 << 4,
    Saturday = 1 << 5,
    Sunday = 1 << 6,
    WorkDays = Monday | Tuesday | Wednesday | Thursday | Friday,
    All = WorkDays | Saturday | Sunday,
}

public static class WeekdaysExtensions
{
    public static Weekdays ToFlag(this DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Weekdays.Monday,
        DayOfWeek.Tuesday => Weekdays.Tuesday,
        DayOfWeek.Wednesday => Weekdays.Wednesday,
        DayOfWeek.Thursday => Weekdays.Thursday,
        DayOfWeek.Friday => Weekdays.Friday,
        DayOfWeek.Saturday => Weekdays.Saturday,
        DayOfWeek.Sunday => Weekdays.Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(day)),
    };

    public static bool Contains(this Weekdays days, DayOfWeek day) => (days & day.ToFlag()) != 0;

    public static bool IsValidMask(this Weekdays days) => days != Weekdays.None && (days & ~Weekdays.All) == 0;
}
