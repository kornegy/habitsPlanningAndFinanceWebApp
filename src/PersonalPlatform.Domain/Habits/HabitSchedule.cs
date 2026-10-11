using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Scheduling;

namespace PersonalPlatform.Domain.Habits;

public enum HabitScheduleType
{
    Daily = 0,
    SpecificWeekdays = 1,
    EveryNDays = 2,
}

/// <summary>Which local calendar dates a habit is expected on.</summary>
public sealed record HabitSchedule
{
    public const int MaxIntervalDays = 365;

    public HabitSchedule(HabitScheduleType type, Weekdays days, int intervalDays)
    {
        Rule.Require(Enum.IsDefined(type), "invalid_schedule", "Unknown schedule type.");
        switch (type)
        {
            case HabitScheduleType.Daily:
                days = Weekdays.None;
                intervalDays = 1;
                break;
            case HabitScheduleType.SpecificWeekdays:
                Rule.Require(days.IsValidMask(), "invalid_schedule", "Select at least one weekday.");
                intervalDays = 1;
                break;
            case HabitScheduleType.EveryNDays:
                Rule.Require(intervalDays is >= 2 and <= MaxIntervalDays, "invalid_schedule", $"Interval must be between 2 and {MaxIntervalDays} days.");
                days = Weekdays.None;
                break;
        }

        Type = type;
        Days = days;
        IntervalDays = intervalDays;
    }

    public HabitScheduleType Type { get; }

    public Weekdays Days { get; }

    public int IntervalDays { get; }

    public bool IsDue(DateOnly date, DateOnly startDate)
    {
        if (date < startDate)
        {
            return false;
        }

        return Type switch
        {
            HabitScheduleType.Daily => true,
            HabitScheduleType.SpecificWeekdays => Days.Contains(date.DayOfWeek),
            HabitScheduleType.EveryNDays => (date.DayNumber - startDate.DayNumber) % IntervalDays == 0,
            _ => false,
        };
    }
}
