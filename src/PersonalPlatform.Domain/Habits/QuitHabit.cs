using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Domain.Habits;

/// <summary>
/// A habit the user wants to give up (smoking, sugar, doom-scrolling...). Progress is the time since the quit date
/// or since the last recorded relapse; relapses are the only events stored, so nothing has to be ticked daily.
/// </summary>
public sealed class QuitHabit : IOwnedEntity
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;

    private QuitHabit()
    {
    }

    public QuitHabit(Guid ownerId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string Color { get; private set; } = ColorHex.Default;

    /// <summary>Local date the user stopped; days are counted from here until the first relapse.</summary>
    public DateOnly QuitDate { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public void Update(string name, string? description, string? color, DateOnly quitDate, DateOnly today, DateTimeOffset now)
    {
        Rule.Require(quitDate <= today, "date_in_future", "The quit date cannot be in the future.");
        Name = Rule.RequireText(name, NameMaxLength, "invalid_name", "Name");
        Description = Rule.OptionalText(description, DescriptionMaxLength, "invalid_description", "Description");
        Color = ColorHex.Normalize(color);
        QuitDate = quitDate;
        UpdatedAt = now;
    }

    /// <summary>Starts counting again from <paramref name="today"/> (the user wants a clean slate).</summary>
    public void Restart(DateOnly today, DateTimeOffset now)
    {
        QuitDate = today;
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

    /// <summary>Records a slip on a local date between the quit date and today.</summary>
    public QuitHabitRelapse Relapse(DateOnly date, string? note, DateOnly today, DateTimeOffset now)
    {
        Rule.Require(!IsArchived, "habit_archived", "Archived habits cannot be changed.");
        Rule.Require(date <= today, "date_in_future", "Cannot record a relapse in the future.");
        Rule.Require(date >= QuitDate, "date_before_quit", "The date is before the quit date.");
        return new QuitHabitRelapse(this, date, note, now);
    }
}

/// <summary>A relapse of a <see cref="QuitHabit"/> on a local date. Unique per (habit, date).</summary>
public sealed class QuitHabitRelapse : IOwnedEntity
{
    public const int NoteMaxLength = 500;

    private QuitHabitRelapse()
    {
    }

    internal QuitHabitRelapse(QuitHabit habit, DateOnly date, string? note, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = habit.OwnerId;
        QuitHabitId = habit.Id;
        Date = date;
        Note = Rule.OptionalText(note, NoteMaxLength, "invalid_note", "Note");
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid QuitHabitId { get; private set; }

    public DateOnly Date { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>Progress of a quit habit at a given day.</summary>
/// <param name="CurrentStreakDays">Whole days since the quit date or the last relapse (0 on that day itself).</param>
/// <param name="LongestStreakDays">Longest run between quit date, relapses and today.</param>
/// <param name="RelapseCount">Relapses on or after the quit date.</param>
/// <param name="LastRelapse">Most recent relapse date, if any.</param>
/// <param name="NextMilestoneDays">Next milestone to reach, or null once the last one is passed.</param>
public sealed record QuitProgress(int CurrentStreakDays, int LongestStreakDays, int RelapseCount, DateOnly? LastRelapse, int? NextMilestoneDays);

public static class QuitHabitStatistics
{
    /// <summary>Milestones in days, shown as goals: a day, three days, a week, two weeks, a month... three years.</summary>
    public static readonly IReadOnlyList<int> Milestones = [1, 3, 7, 14, 30, 60, 90, 180, 365, 730, 1095];

    public static QuitProgress Calculate(DateOnly quitDate, IEnumerable<DateOnly> relapses, DateOnly today)
    {
        // Relapses before the quit date (e.g. after the quit date was moved forward) or in the future do not count.
        var points = relapses.Where(d => d >= quitDate && d <= today).Distinct().Order().ToList();
        var start = quitDate <= today ? quitDate : today;
        var longest = 0;
        var previous = start;
        foreach (var relapse in points)
        {
            longest = Math.Max(longest, relapse.DayNumber - previous.DayNumber);
            previous = relapse;
        }

        var current = today.DayNumber - previous.DayNumber;
        longest = Math.Max(longest, current);
        int? next = Milestones.Cast<int?>().FirstOrDefault(m => m > current);
        return new QuitProgress(current, longest, points.Count, points.Count == 0 ? null : points[^1], next);
    }
}
