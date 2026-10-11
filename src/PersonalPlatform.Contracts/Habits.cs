using PersonalPlatform.Contracts.Tasks;

namespace PersonalPlatform.Contracts.Habits;

public enum HabitScheduleType
{
    Daily = 0,
    SpecificWeekdays = 1,
    EveryNDays = 2,
}

public sealed record HabitDto(
    Guid Id,
    string Name,
    string? Description,
    string Color,
    Guid? CategoryId,
    HabitScheduleType ScheduleType,
    Weekdays Days,
    int IntervalDays,
    DateOnly StartDate,
    int? TargetStreak,
    TimeOnly? ReminderTime,
    bool IsArchived,
    bool IsDueToday,
    bool CompletedToday,
    int CurrentStreak,
    int LongestStreak,
    uint Version);

public sealed record CreateHabitRequest(
    string Name,
    string? Description,
    string? Color,
    Guid? CategoryId,
    HabitScheduleType ScheduleType,
    Weekdays Days,
    int IntervalDays,
    DateOnly? StartDate,
    int? TargetStreak,
    TimeOnly? ReminderTime);

public sealed record UpdateHabitRequest(
    string Name,
    string? Description,
    string? Color,
    Guid? CategoryId,
    HabitScheduleType ScheduleType,
    Weekdays Days,
    int IntervalDays,
    DateOnly StartDate,
    int? TargetStreak,
    TimeOnly? ReminderTime,
    uint Version);

public sealed record HabitCompletionResult(Guid HabitId, DateOnly Date, bool Completed, int CurrentStreak);

public sealed record HabitHistoryDto(Guid HabitId, DateOnly From, DateOnly To, IReadOnlyList<DateOnly> ScheduledDates, IReadOnlyList<DateOnly> CompletedDates);

public sealed record PeriodStatDto(DateOnly PeriodStart, DateOnly PeriodEnd, int Scheduled, int Completed, decimal CompletionRate);

public sealed record HabitStatsDto(
    Guid HabitId,
    DateOnly From,
    DateOnly To,
    int CurrentStreak,
    int LongestStreak,
    int Scheduled,
    int Completed,
    decimal CompletionRate,
    IReadOnlyList<PeriodStatDto> Weeks,
    IReadOnlyList<PeriodStatDto> Months);
