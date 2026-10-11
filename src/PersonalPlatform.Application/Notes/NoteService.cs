using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Application.Organization;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Notes;
using PersonalPlatform.Contracts.Organization;
using PersonalPlatform.Domain.Notes;

namespace PersonalPlatform.Application.Notes;

/// <summary>
/// Notes with tags, pinning, archive, full-text search and bounded revision history. Content is plain text;
/// it is stored and returned verbatim and must be rendered as text by clients.
/// </summary>
public sealed class NoteService(IAppDbContext db, ICurrentUser currentUser, UserClock clock, ITextSearch search)
{
    public const int MaxNotesPerUser = 10_000;
    private const int SnippetLength = 160;

    private Guid Owner => currentUser.UserId;

    public async Task<PagedResult<NoteSummaryDto>> ListAsync(NoteQuery query, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var notes = db.Notes.AsNoTracking().Where(n => n.OwnerId == owner && (query.Archived ? n.ArchivedAt != null : n.ArchivedAt == null));
        if (query.TagId is { } tagId)
        {
            notes = notes.Where(n => n.Tags.Any(t => t.TagId == tagId));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            notes = search.MatchNotes(notes, query.Search);
        }

        IOrderedQueryable<Note> ordered = query.Sort switch
        {
            NoteSort.CreatedDesc => notes.OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.CreatedAt),
            NoteSort.TitleAsc => notes.OrderByDescending(n => n.IsPinned).ThenBy(n => n.Title),
            _ => notes.OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.UpdatedAt),
        };

        var tags = db.Tags;
        return await ordered.ThenBy(n => n.Id)
            .Select(n => new NoteSummaryDto(
                n.Id,
                n.Title,
                n.Content.Substring(0, n.Content.Length < SnippetLength ? n.Content.Length : SnippetLength),
                n.IsPinned,
                n.ArchivedAt != null,
                tags.Where(t => n.Tags.Select(x => x.TagId).Contains(t.Id)).OrderBy(t => t.NormalizedName).Select(t => new TagDto(t.Id, t.Name)).ToList(),
                n.UpdatedAt))
            .ToPagedResultAsync(query.Page, query.PageSize, cancellationToken);
    }

    public async Task<NoteDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var tags = db.Tags;
        return await db.Notes.AsNoTracking()
            .Where(n => n.Id == id && n.OwnerId == owner)
            .Select(n => new NoteDto(
                n.Id,
                n.Title,
                n.Content,
                n.IsPinned,
                n.ArchivedAt != null,
                tags.Where(t => n.Tags.Select(x => x.TagId).Contains(t.Id)).OrderBy(t => t.NormalizedName).Select(t => new TagDto(t.Id, t.Name)).ToList(),
                n.CreatedAt,
                n.UpdatedAt,
                n.Version))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException();
    }

    public async Task<NoteDto> CreateAsync(CreateNoteRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        if (await db.Notes.CountAsync(n => n.OwnerId == owner, cancellationToken) >= MaxNotesPerUser)
        {
            throw new ConflictException("limit_reached", $"At most {MaxNotesPerUser} notes are allowed.");
        }

        var now = clock.UtcNow;
        var tags = await OrganizationService.LoadOwnedTagsAsync(db, owner, request.TagIds, cancellationToken);
        var note = new Note(owner, now);
        note.Edit(request.Title, request.Content, now);
        note.SetPinned(request.IsPinned, now);
        note.ReplaceTags(tags.ToList());
        db.Notes.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(note.Id, cancellationToken);
    }

    /// <summary>
    /// Saves an edit (autosave). Requires the version last read; a concurrent change elsewhere yields 409 so that no
    /// edit is silently overwritten. The previous content is kept as a revision (bounded history).
    /// </summary>
    public async Task<NoteDto> UpdateAsync(Guid id, UpdateNoteRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var now = clock.UtcNow;
        var note = await db.Notes.Include(n => n.Tags).FirstOrDefaultAsync(n => n.Id == id && n.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
        db.SetExpectedVersion(note, request.Version);
        var tags = await OrganizationService.LoadOwnedTagsAsync(db, owner, request.TagIds, cancellationToken);

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var revision = note.Edit(request.Title, request.Content, now);
        note.ReplaceTags(tags.ToList());
        if (revision is not null)
        {
            db.NoteRevisions.Add(revision);
        }

        await db.SaveChangesAsync(cancellationToken);
        await PruneRevisionsAsync(owner, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<NoteDto> SetPinnedAsync(Guid id, bool pinned, CancellationToken cancellationToken)
    {
        var note = await LoadAsync(id, cancellationToken);
        note.SetPinned(pinned, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<NoteDto> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken)
    {
        var note = await LoadAsync(id, cancellationToken);
        if (archived)
        {
            note.Archive(clock.UtcNow);
        }
        else
        {
            note.Restore(clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Permanently deletes the note with its tags and revisions.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        if (await db.Notes.Where(n => n.Id == id && n.OwnerId == owner).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            throw new NotFoundException();
        }
    }

    public async Task<IReadOnlyList<NoteRevisionDto>> RevisionsAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        if (!await db.Notes.AnyAsync(n => n.Id == id && n.OwnerId == owner, cancellationToken))
        {
            throw new NotFoundException();
        }

        return await db.NoteRevisions.AsNoTracking()
            .Where(r => r.NoteId == id && r.OwnerId == owner)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new NoteRevisionDto(r.Id, r.Title, r.Content, r.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task PruneRevisionsAsync(Guid owner, Guid noteId, CancellationToken cancellationToken)
    {
        var keep = db.NoteRevisions
            .Where(r => r.NoteId == noteId && r.OwnerId == owner)
            .OrderByDescending(r => r.CreatedAt)
            .Take(Note.MaxRevisions)
            .Select(r => r.Id);
        await db.NoteRevisions
            .Where(r => r.NoteId == noteId && r.OwnerId == owner && !keep.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<Note> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        return await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
    }
}

public sealed class CreateNoteRequestValidator : AbstractValidator<CreateNoteRequest>
{
    public CreateNoteRequestValidator()
    {
        RuleFor(x => x.Title).MaximumLength(Note.TitleMaxLength);
        RuleFor(x => x.Content).MaximumLength(Note.ContentMaxLength);
        RuleFor(x => x.TagIds).DistinctIds(Note.MaxTags);
    }
}

public sealed class UpdateNoteRequestValidator : AbstractValidator<UpdateNoteRequest>
{
    public UpdateNoteRequestValidator()
    {
        RuleFor(x => x.Title).MaximumLength(Note.TitleMaxLength);
        RuleFor(x => x.Content).MaximumLength(Note.ContentMaxLength);
        RuleFor(x => x.TagIds).DistinctIds(Note.MaxTags);
    }
}

public sealed class NoteQueryValidator : AbstractValidator<NoteQuery>
{
    public NoteQueryValidator()
    {
        RuleFor(x => x.Sort).IsInEnum();
        RuleFor(x => x.Search).ValidSearch();
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}
