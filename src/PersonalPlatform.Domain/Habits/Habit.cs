using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Scheduling;

namespace PersonalPlatform.Domain.Habits;

public sealed class Habit : IOwnedEntity
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;
    public const int MaxTargetStreak = 3650;

    private Habit()
    {
    }

    public Habit(Guid ownerId, DateOnly startDate, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        StartDate = startDate;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string Color { get; private set; } = ColorHex.Default;

    public Guid? CategoryId { get; private set; }

    public HabitScheduleType ScheduleType { get; private set; }

    public Weekdays Days { get; private set; }

    public int IntervalDays { get; private set; } = 1;

    /// <summary>First local date the habit is tracked; completions before it are rejected.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Optional goal: desired streak length in scheduled days.</summary>
    public int? TargetStreak { get; private set; }

    /// <summary>Optional local time of day for the in-app reminder; null disables reminders.</summary>
    public TimeOnly? ReminderTime { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public HabitSchedule Schedule => new(ScheduleType, Days, IntervalDays);

    public void Update(
        string name,
        string? description,
        string? color,
        Guid? categoryId,
        HabitSchedule schedule,
        DateOnly startDate,
        int? targetStreak,
        TimeOnly? reminderTime,
        DateTimeOffset now)
    {
        Rule.Require(targetStreak is null or (>= 1 and <= MaxTargetStreak), "invalid_target", $"Target streak must be between 1 and {MaxTargetStreak}.");
        Name = Rule.RequireText(name, NameMaxLength, "invalid_name", "Name");
        Description = Rule.OptionalText(description, DescriptionMaxLength, "invalid_description", "Description");
        Color = ColorHex.Normalize(color);
        CategoryId = categoryId;
        ScheduleType = schedule.Type;
        Days = schedule.Days;
        IntervalDays = schedule.IntervalDays;
        StartDate = startDate;
        TargetStreak = targetStreak;
        ReminderTime = reminderTime;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        ArchivedAt ??= now;
        UpdatedAt = now;
    }

    public void Restore(DateTimeOffset now)
    {
        ArchivedAt = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Validates that a completion may be recorded for <paramref name="date"/>: the habit is active, the
    /// date is scheduled, not before the start date and not in the owner's future.
    /// </summary>
    public HabitCompletion Complete(DateOnly date, DateOnly today, DateTimeOffset now)
    {
        Rule.Require(!IsArchived, "habit_archived", "Archived habits cannot be checked off.");
        Rule.Require(date <= today, "date_in_future", "Cannot check off a future date.");
        Rule.Require(date >= StartDate, "date_before_start", "Date is before the habit start date.");
        Rule.Require(Schedule.IsDue(date, StartDate), "date_not_scheduled", "The habit is not scheduled on this date.");
        return new HabitCompletion(this, date, now);
    }
}

/// <summary>One check-off of a habit for a local calendar date. Unique per (habit, date).</summary>
public sealed class HabitCompletion : IOwnedEntity
{
    private HabitCompletion()
    {
    }

    internal HabitCompletion(Habit habit, DateOnly date, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = habit.OwnerId;
        HabitId = habit.Id;
        Date = date;
        CompletedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid HabitId { get; private set; }

    public DateOnly Date { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }
}
