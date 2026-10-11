using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Domain.Scheduling;

public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3,
}

/// <summary>
/// Calendar recurrence rule shared by recurring tasks and recurring financial transactions.
/// </summary>
/// <remarks>
/// Rules (all on local calendar dates, so DST transitions cannot shift occurrences):
/// <list type="bullet">
/// <item>Daily: every <c>Interval</c> days counted from <c>StartDate</c>.</item>
/// <item>Weekly: on the selected weekdays of every <c>Interval</c>-th ISO week counted from the week of
/// <c>StartDate</c>; when no weekday is selected, the weekday of <c>StartDate</c> is used.</item>
/// <item>Monthly: on the day-of-month of <c>StartDate</c>, clamped to shorter months.</item>
/// <item>Yearly: on the month/day of <c>StartDate</c>; Feb 29 is clamped to Feb 28 in common years.</item>
/// </list>
/// Occurrences never precede <c>StartDate</c> and never follow <c>EndDate</c> (when set).
/// </remarks>
public sealed record RecurrencePattern
{
    public const int MaxInterval = 365;

    // Bounds iteration so that a malformed or extreme rule can never cause unbounded CPU use.
    private const int MaxSearchSteps = 5_000;

    public RecurrencePattern(RecurrenceFrequency frequency, int interval, Weekdays days, DateOnly startDate, DateOnly? endDate)
    {
        Rule.Require(Enum.IsDefined(frequency), "invalid_recurrence", "Unknown recurrence frequency.");
        Rule.Require(interval is >= 1 and <= MaxInterval, "invalid_recurrence", $"Interval must be between 1 and {MaxInterval}.");
        Rule.Require(endDate is null || endDate >= startDate, "invalid_recurrence", "End date must not precede start date.");
        Rule.Require((days & ~Weekdays.All) == 0, "invalid_recurrence", "Unknown weekday flags.");

        Frequency = frequency;
        Interval = interval;
        Days = frequency == RecurrenceFrequency.Weekly
            ? (days == Weekdays.None ? startDate.DayOfWeek.ToFlag() : days)
            : Weekdays.None;
        StartDate = startDate;
        EndDate = endDate;
    }

    public RecurrenceFrequency Frequency { get; }

    public int Interval { get; }

    public Weekdays Days { get; }

    public DateOnly StartDate { get; }

    public DateOnly? EndDate { get; }

    public bool OccursOn(DateOnly date)
    {
        if (date < StartDate || (EndDate is { } end && date > end))
        {
            return false;
        }

        return Frequency switch
        {
            RecurrenceFrequency.Daily => (date.DayNumber - StartDate.DayNumber) % Interval == 0,
            RecurrenceFrequency.Weekly => Days.Contains(date.DayOfWeek) && WeekIndex(date) % Interval == 0,
            RecurrenceFrequency.Monthly =>
                CalendarMath.MonthsBetween(StartDate, date) % Interval == 0
                && date == CalendarMath.ClampedDate(date.Year, date.Month, StartDate.Day),
            RecurrenceFrequency.Yearly =>
                (date.Year - StartDate.Year) % Interval == 0
                && date.Month == StartDate.Month
                && date == CalendarMath.ClampedDate(date.Year, date.Month, StartDate.Day),
            _ => false,
        };
    }

    /// <summary>First occurrence on or after <paramref name="date"/>, or null when the series has ended.</summary>
    public DateOnly? FirstOnOrAfter(DateOnly date)
    {
        var candidate = date < StartDate ? StartDate : date;
        return Frequency switch
        {
            RecurrenceFrequency.Daily => Bounded(AlignDaily(candidate)),
            RecurrenceFrequency.Weekly => ScanDays(candidate),
            RecurrenceFrequency.Monthly => ScanMonths(candidate, monthStep: 1),
            RecurrenceFrequency.Yearly => ScanMonths(candidate, monthStep: 12),
            _ => null,
        };
    }

    /// <summary>First occurrence strictly after <paramref name="date"/>.</summary>
    public DateOnly? NextAfter(DateOnly date) =>
        date == DateOnly.MaxValue ? null : FirstOnOrAfter(date.AddDays(1));

    /// <summary>All occurrences in the inclusive range, capped at <paramref name="maxCount"/>.</summary>
    public IReadOnlyList<DateOnly> Between(DateOnly from, DateOnly to, int maxCount)
    {
        var result = new List<DateOnly>();
        var next = FirstOnOrAfter(from);
        while (next is { } occurrence && occurrence <= to && result.Count < maxCount)
        {
            result.Add(occurrence);
            next = NextAfter(occurrence);
        }

        return result;
    }

    private int WeekIndex(DateOnly date) =>
        (CalendarMath.StartOfIsoWeek(date).DayNumber - CalendarMath.StartOfIsoWeek(StartDate).DayNumber) / 7;

    private DateOnly AlignDaily(DateOnly candidate)
    {
        var remainder = (candidate.DayNumber - StartDate.DayNumber) % Interval;
        return remainder == 0 ? candidate : candidate.AddDays(Interval - remainder);
    }

    private DateOnly? ScanDays(DateOnly candidate)
    {
        for (var step = 0; step < MaxSearchSteps; step++)
        {
            if (EndDate is { } end && candidate > end)
            {
                return null;
            }

            if (OccursOn(candidate))
            {
                return candidate;
            }

            if (candidate == DateOnly.MaxValue)
            {
                return null;
            }

            candidate = candidate.AddDays(1);
        }

        return null;
    }

    private DateOnly? ScanMonths(DateOnly candidate, int monthStep)
    {
        // Start from the occurrence month of the candidate's month and move forward month by month
        // (or year by year) until an occurrence on/after the candidate is found.
        var monthCursor = new DateOnly(candidate.Year, candidate.Month, 1);
        if (Frequency == RecurrenceFrequency.Yearly)
        {
            monthCursor = new DateOnly(candidate.Year, StartDate.Month, 1);
            if (monthCursor < new DateOnly(candidate.Year, candidate.Month, 1))
            {
                monthCursor = monthCursor.AddYears(1);
            }
        }

        for (var step = 0; step < MaxSearchSteps; step++)
        {
            var occurrence = CalendarMath.ClampedDate(monthCursor.Year, monthCursor.Month, StartDate.Day);
            if (EndDate is { } end && occurrence > end)
            {
                return null;
            }

            if (occurrence >= candidate && OccursOn(occurrence))
            {
                return occurrence;
            }

            if (monthCursor.Year >= DateOnly.MaxValue.Year - 1)
            {
                return null;
            }

            monthCursor = monthCursor.AddMonths(monthStep);
        }

        return null;
    }

    private DateOnly? Bounded(DateOnly candidate) =>
        EndDate is { } end && candidate > end ? null : candidate;
}
