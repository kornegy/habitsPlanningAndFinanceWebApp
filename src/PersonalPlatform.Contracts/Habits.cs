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

/// <summary>Outcome of a progress reset: how many habits restart today and how many check-offs were removed.</summary>
public sealed record HabitResetResult(int HabitsReset, int CompletionsRemoved);

/// <summary>A habit the user is quitting. All counters are computed on the server from the quit date and relapses.</summary>
public sealed record QuitHabitDto(
    Guid Id,
    string Name,
    string? Description,
    string Color,
    DateOnly QuitDate,
    bool IsArchived,
    int CurrentStreakDays,
    int LongestStreakDays,
    int RelapseCount,
    DateOnly? LastRelapse,
    int? NextMilestoneDays,
    uint Version);

public sealed record QuitHabitRelapseDto(DateOnly Date, string? Note);

/// <summary>Quit habit with its most recent relapses (newest first, at most 500).</summary>
public sealed record QuitHabitDetailDto(QuitHabitDto Habit, IReadOnlyList<QuitHabitRelapseDto> Relapses);

public sealed record CreateQuitHabitRequest(string Name, string? Description, string? Color, DateOnly? QuitDate);

public sealed record UpdateQuitHabitRequest(string Name, string? Description, string? Color, DateOnly QuitDate, uint Version);

public sealed record LogRelapseRequest(DateOnly Date, string? Note);
