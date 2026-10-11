using PersonalPlatform.Contracts.Organization;

namespace PersonalPlatform.Contracts.Notes;

public enum NoteSort
{
    UpdatedDesc = 0,
    CreatedDesc = 1,
    TitleAsc = 2,
}

/// <summary>Full note. <see cref="Content"/> is plain text and must be rendered as text, never as HTML.</summary>
public sealed record NoteDto(
    Guid Id,
    string Title,
    string Content,
    bool IsPinned,
    bool IsArchived,
    IReadOnlyList<TagDto> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version);

public sealed record NoteSummaryDto(
    Guid Id,
    string Title,
    string Snippet,
    bool IsPinned,
    bool IsArchived,
    IReadOnlyList<TagDto> Tags,
    DateTimeOffset UpdatedAt);

public sealed record CreateNoteRequest(string? Title, string? Content, IReadOnlyList<Guid>? TagIds, bool IsPinned);

/// <summary>Autosave payload. A stale <see cref="Version"/> yields 409 so concurrent edits are never silently lost.</summary>
public sealed record UpdateNoteRequest(string? Title, string? Content, IReadOnlyList<Guid>? TagIds, uint Version);

public sealed record SetPinnedRequest(bool IsPinned);

public sealed record SetArchivedRequest(bool IsArchived);

public sealed record NoteQuery(
    string? Search = null,
    Guid? TagId = null,
    bool Archived = false,
    NoteSort Sort = NoteSort.UpdatedDesc,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

public sealed record NoteRevisionDto(Guid Id, string Title, string Content, DateTimeOffset CreatedAt);

public sealed record JournalEntryDto(Guid Id, DateOnly EntryDate, string Content, int? Mood, DateTimeOffset UpdatedAt, uint Version);

/// <summary>Creates or updates the entry for a date. <see cref="Version"/> is required when updating.</summary>
public sealed record SaveJournalEntryRequest(string? Content, int? Mood, uint? Version);

public sealed record JournalQuery(DateOnly? From = null, DateOnly? To = null, string? Search = null, int Page = 1, int PageSize = Paging.DefaultPageSize);
