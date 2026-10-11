using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Domain.Planner;

/// <summary>
/// A planned interval on the user's calendar, stored as absolute UTC instants so DST changes never alter
/// its real duration. The interval is half-open: [StartsAt, EndsAt).
/// </summary>
public sealed class TimeBlock : IOwnedEntity
{
    public const int TitleMaxLength = 200;
    public const int NotesMaxLength = 2000;
    public static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);

    private TimeBlock()
    {
    }

    public TimeBlock(Guid ownerId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Notes { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public Guid? TaskId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public void Update(string title, string? notes, DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? taskId, DateTimeOffset now)
    {
        Title = Rule.RequireText(title, TitleMaxLength, "invalid_title", "Title");
        Notes = Rule.OptionalText(notes, NotesMaxLength, "invalid_notes", "Notes");
        Move(startsAt, endsAt, now);
        TaskId = taskId;
    }

    public void Move(DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset now)
    {
        var duration = endsAt - startsAt;
        Rule.Require(duration >= MinDuration, "invalid_interval", "A time block must last at least 5 minutes and end after it starts.");
        Rule.Require(duration <= MaxDuration, "invalid_interval", "A time block cannot be longer than 24 hours.");
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();
        UpdatedAt = now;
    }

    public static bool Overlap(DateTimeOffset aStart, DateTimeOffset aEnd, DateTimeOffset bStart, DateTimeOffset bEnd) =>
        aStart < bEnd && bStart < aEnd;
}
