using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Domain.Notes;

/// <summary>Diary entry bound to one local calendar date; at most one entry per user per date.</summary>
public sealed class JournalEntry : IOwnedEntity
{
    public const int ContentMaxLength = 50_000;
    public const int MinMood = 1;
    public const int MaxMood = 5;

    private JournalEntry()
    {
    }

    public JournalEntry(Guid ownerId, DateOnly entryDate, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        EntryDate = entryDate;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public DateOnly EntryDate { get; private set; }

    public string Content { get; private set; } = string.Empty;

    /// <summary>Optional self-reported mood, 1 (low) to 5 (high).</summary>
    public int? Mood { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public void Edit(string? content, int? mood, DateTimeOffset now)
    {
        var text = content ?? string.Empty;
        Rule.Require(text.Length <= ContentMaxLength, "content_too_long", $"Content must be at most {ContentMaxLength} characters.");
        Rule.Require(mood is null or (>= MinMood and <= MaxMood), "invalid_mood", "Mood must be between 1 and 5.");
        Content = text;
        Mood = mood;
        UpdatedAt = now;
    }
}
