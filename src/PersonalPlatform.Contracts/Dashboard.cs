using PersonalPlatform.Contracts.Finance;
using PersonalPlatform.Contracts.Planner;

namespace PersonalPlatform.Contracts.Dashboard;

public sealed record DashboardHabitDto(Guid Id, string Name, string Color, bool Completed, int CurrentStreak, int? TargetStreak);

public sealed record DashboardTaskDto(Guid Id, string Title, Tasks.TaskPriority Priority, DateOnly? DueDate, TimeOnly? DueTime, bool IsDone, bool IsOverdue);

public sealed record DayProgressDto(int TasksDone, int TasksTotal, int HabitsDone, int HabitsTotal, int BlocksDone, int BlocksTotal);

public sealed record DashboardDto(
    string DisplayName,
    DateOnly Today,
    string TimeZoneId,
    IReadOnlyList<DashboardHabitDto> Habits,
    IReadOnlyList<DashboardTaskDto> TasksToday,
    int OverdueCount,
    IReadOnlyList<TimeBlockDto> UpcomingBlocks,
    IReadOnlyList<CurrencyFlowDto> MonthCashFlow,
    IReadOnlyList<BudgetUsageDto> BudgetAlerts,
    DayProgressDto Progress);

public enum ReminderKind
{
    Task = 0,
    Habit = 1,
}

public sealed record ReminderDto(ReminderKind Kind, Guid Id, string Title, DateTimeOffset DueAt);
