using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts.Dashboard;
using PersonalPlatform.Domain.Tasks;
using PersonalPlatform.Domain.Users;

namespace PersonalPlatform.Application.Dashboard;

/// <summary>
/// In-app reminders (polled by the client): task reminders whose time has come and that were not dismissed,
/// and habits with a reminder time that are due today, past their time and not yet checked off.
/// </summary>
public sealed class ReminderService(IAppDbContext db, ICurrentUser currentUser, UserClock clock)
{
    private const int MaxReminders = 20;

    public async Task<IReadOnlyList<ReminderDto>> DueAsync(CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;
        var context = await clock.GetAsync(cancellationToken);
        var now = context.Now;

        var taskReminders = await db.Tasks.AsNoTracking()
            .Where(t => t.OwnerId == owner && t.ReminderAt != null && t.ReminderAt <= now && t.ReminderDismissedAt == null && t.Status != TaskItemStatus.Done)
            .OrderBy(t => t.ReminderAt)
            .Take(MaxReminders)
            .Select(t => new ReminderDto(ReminderKind.Task, t.Id, t.Title, t.ReminderAt!.Value))
            .ToListAsync(cancellationToken);

        var completedToday = await db.HabitCompletions.AsNoTracking()
            .Where(c => c.OwnerId == owner && c.Date == context.Today)
            .Select(c => c.HabitId)
            .ToListAsync(cancellationToken);
        var habits = await db.Habits.AsNoTracking()
            .Where(h => h.OwnerId == owner && h.ArchivedAt == null && h.ReminderTime != null && !completedToday.Contains(h.Id))
            .ToListAsync(cancellationToken);
        var habitReminders = habits
            .Where(h => h.Schedule.IsDue(context.Today, h.StartDate))
            .Select(h => new ReminderDto(ReminderKind.Habit, h.Id, h.Name, TimeZones.ToUtc(context.Today, h.ReminderTime!.Value, context.Zone)))
            .Where(r => r.DueAt <= now);

        return taskReminders.Concat(habitReminders).OrderBy(r => r.DueAt).Take(MaxReminders).ToList();
    }
}
