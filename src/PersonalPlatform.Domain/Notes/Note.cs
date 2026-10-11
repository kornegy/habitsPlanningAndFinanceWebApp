using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Organization;

namespace PersonalPlatform.Domain.Notes;

/// <summary>
/// Free-form note. Content is stored and returned as plain text; it is never rendered as HTML.
/// </summary>
public sealed class Note : IOwnedEntity
{
    public const int TitleMaxLength = 200;
    public const int ContentMaxLength = 100_000;
    public const int MaxTags = 10;

    /// <summary>Revisions kept per note; older ones are pruned so history cannot grow without bound.</summary>
    public const int MaxRevisions = 20;

    private readonly List<NoteTag> _tags = [];

    private Note()
    {
    }

    public Note(Guid ownerId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Content { get; private set; } = string.Empty;

    public bool IsPinned { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<NoteTag> Tags => _tags;

    public bool IsArchived => ArchivedAt is not null;

    /// <summary>Returns a revision snapshot of the previous content when it actually changed.</summary>
    public NoteRevision? Edit(string? title, string? content, DateTimeOffset now)
    {
        var newTitle = Rule.OptionalText(title, TitleMaxLength, "invalid_title", "Title") ?? string.Empty;
        var newContent = content ?? string.Empty;
        Rule.Require(newContent.Length <= ContentMaxLength, "content_too_long", $"Content must be at most {ContentMaxLength} characters.");

        if (newTitle == Title && newContent == Content)
        {
            return null;
        }

        var revision = Title.Length > 0 || Content.Length > 0 ? new NoteRevision(this, now) : null;
        Title = newTitle;
        Content = newContent;
        UpdatedAt = now;
        return revision;
    }

    public void SetPinned(bool pinned, DateTimeOffset now)
    {
        IsPinned = pinned;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        ArchivedAt ??= now;
        IsPinned = false;
        UpdatedAt = now;
    }

    public void Restore(DateTimeOffset now)
    {
        ArchivedAt = null;
        UpdatedAt = now;
    }

    public void ReplaceTags(IReadOnlyCollection<Tag> tags)
    {
        Rule.Require(tags.Count <= MaxTags, "too_many_tags", $"A note can have at most {MaxTags} tags.");
        Rule.Require(tags.All(t => t.OwnerId == OwnerId), "tag_not_found", "Tag not found.");
        _tags.RemoveAll(existing => tags.All(t => t.Id != existing.TagId));
        foreach (var tag in tags.Where(t => _tags.All(existing => existing.TagId != t.Id)))
        {
            _tags.Add(new NoteTag(OwnerId, Id, tag.Id));
        }
    }
}

public sealed class NoteTag : IOwnedEntity
{
    private NoteTag()
    {
    }

    internal NoteTag(Guid ownerId, Guid noteId, Guid tagId)
    {
        OwnerId = ownerId;
        NoteId = noteId;
        TagId = tagId;
    }

    public Guid OwnerId { get; private set; }

    public Guid NoteId { get; private set; }

    public Guid TagId { get; private set; }
}

/// <summary>Snapshot of a note before an edit.</summary>
public sealed class NoteRevision : IOwnedEntity
{
    private NoteRevision()
    {
    }

    internal NoteRevision(Note note, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = note.OwnerId;
        NoteId = note.Id;
        Title = note.Title;
        Content = note.Content;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid NoteId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Content { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
}
