using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Application.Finance;
using PersonalPlatform.Application.Habits;
using PersonalPlatform.Contracts.Dashboard;
using PersonalPlatform.Contracts.Finance;
using PersonalPlatform.Domain.Scheduling;
using PersonalPlatform.Domain.Tasks;
using PersonalPlatform.Domain.Users;

namespace PersonalPlatform.Application.Dashboard;

/// <summary>
/// Everything the home screen needs in one round trip, computed for the current owner and their local "today".
/// </summary>
public sealed class DashboardService(
    IAppDbContext db,
    ICurrentUser currentUser,
    UserClock clock,
    HabitService habits,
    FinanceReportService reports,
    BudgetService budgets)
{
    private const int MaxTasksShown = 20;
    private const int MaxBlocksShown = 5;

    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;
        var context = await clock.GetAsync(cancellationToken);
        var today = context.Today;

        var habitList = (await habits.ListAsync(includeArchived: false, cancellationToken))
            .Where(h => h.IsDueToday)
            .Select(h => new DashboardHabitDto(h.Id, h.Name, h.Color, h.CompletedToday, h.CurrentStreak, h.TargetStreak))
            .ToList();

        var tasksToday = await db.Tasks.AsNoTracking()
            .Where(t => t.OwnerId == owner && (t.DueDate == today || (t.DueDate < today && t.Status != TaskItemStatus.Done)))
            .OrderBy(t => t.Status == TaskItemStatus.Done).ThenBy(t => t.DueDate).ThenBy(t => t.DueTime).ThenByDescending(t => t.Priority)
            .Take(MaxTasksShown)
            .Select(t => new DashboardTaskDto(t.Id, t.Title, (Contracts.Tasks.TaskPriority)t.Priority, t.DueDate, t.DueTime, t.Status == TaskItemStatus.Done, t.Status != TaskItemStatus.Done && t.DueDate < today))
            .ToListAsync(cancellationToken);
        var overdueCount = await db.Tasks.CountAsync(t => t.OwnerId == owner && t.DueDate < today && t.Status != TaskItemStatus.Done, cancellationToken);
        var dueToday = await db.Tasks.Where(t => t.OwnerId == owner && t.DueDate == today)
            .GroupBy(t => t.Status == TaskItemStatus.Done)
            .Select(g => new { Done = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var dayStart = TimeZones.StartOfDayUtc(today, context.Zone);
        var dayEnd = TimeZones.StartOfDayUtc(today.AddDays(1), context.Zone);
        var blocksToday = await db.TimeBlocks.AsNoTracking()
            .Where(b => b.OwnerId == owner && b.StartsAt < dayEnd && b.EndsAt > dayStart)
            .OrderBy(b => b.StartsAt)
            .Select(b => new Contracts.Planner.TimeBlockDto(
                b.Id, b.Title, b.Notes, b.StartsAt, b.EndsAt, b.TaskId,
                db.Tasks.Where(t => t.Id == b.TaskId && t.OwnerId == owner).Select(t => t.Title).FirstOrDefault(),
                db.Tasks.Any(t => t.Id == b.TaskId && t.OwnerId == owner && t.Status == TaskItemStatus.Done),
                b.Version))
            .ToListAsync(cancellationToken);
        var upcoming = blocksToday.Where(b => b.EndsAt > context.Now).Take(MaxBlocksShown).ToList();

        var monthStart = CalendarMath.StartOfMonth(today);
        var cashFlow = await reports.PeriodSummaryAsync(monthStart, CalendarMath.EndOfMonth(today), cancellationToken);
        var budgetAlerts = (await budgets.ReportAsync(today.Year, today.Month, cancellationToken)).Items
            .Where(b => b.State != BudgetState.Ok)
            .ToList();

        var progress = new DayProgressDto(
            TasksDone: dueToday.Where(g => g.Done).Sum(g => g.Count),
            TasksTotal: dueToday.Sum(g => g.Count),
            HabitsDone: habitList.Count(h => h.Completed),
            HabitsTotal: habitList.Count,
            BlocksDone: blocksToday.Count(b => b.EndsAt <= context.Now),
            BlocksTotal: blocksToday.Count);

        return new DashboardDto(
            context.Profile.DisplayName,
            today,
            context.Profile.TimeZoneId,
            habitList,
            tasksToday,
            overdueCount,
            upcoming,
            cashFlow.Currencies,
            budgetAlerts,
            progress);
    }
}
