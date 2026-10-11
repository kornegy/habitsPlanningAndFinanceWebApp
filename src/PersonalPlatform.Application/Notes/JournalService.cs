using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Notes;
using PersonalPlatform.Domain.Notes;

namespace PersonalPlatform.Application.Notes;

/// <summary>Diary: one entry per local date, created or updated with an idempotent PUT.</summary>
public sealed class JournalService(IAppDbContext db, ICurrentUser currentUser, UserClock clock, ITextSearch search)
{
    private Guid Owner => currentUser.UserId;

    public async Task<PagedResult<JournalEntryDto>> ListAsync(JournalQuery query, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var entries = db.JournalEntries.AsNoTracking().Where(j => j.OwnerId == owner);
        if (query.From is { } from)
        {
            entries = entries.Where(j => j.EntryDate >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(j => j.EntryDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            entries = search.MatchJournal(entries, query.Search);
        }

        return await entries.OrderByDescending(j => j.EntryDate)
            .Select(j => new JournalEntryDto(j.Id, j.EntryDate, j.Content, j.Mood, j.UpdatedAt, j.Version))
            .ToPagedResultAsync(query.Page, query.PageSize, cancellationToken);
    }

    public async Task<JournalEntryDto> GetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var owner = Owner;
        return await db.JournalEntries.AsNoTracking()
            .Where(j => j.OwnerId == owner && j.EntryDate == date)
            .Select(j => new JournalEntryDto(j.Id, j.EntryDate, j.Content, j.Mood, j.UpdatedAt, j.Version))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException();
    }

    /// <summary>
    /// Creates the entry for <paramref name="date"/> or updates it. Updating requires the last read version so a
    /// concurrent edit (another tab or device) is reported as 409 instead of being overwritten.
    /// </summary>
    public async Task<JournalEntryDto> SaveAsync(DateOnly date, SaveJournalEntryRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var today = (await clock.GetAsync(cancellationToken)).Today;
        if (date > today)
        {
            throw new Domain.Common.DomainRuleException("date_in_future", "Journal entries cannot be written for future dates.");
        }

        var now = clock.UtcNow;
        var entry = await db.JournalEntries.FirstOrDefaultAsync(j => j.OwnerId == owner && j.EntryDate == date, cancellationToken);
        if (entry is null)
        {
            entry = new JournalEntry(owner, date, now);
            db.JournalEntries.Add(entry);
        }
        else
        {
            if (request.Version is not { } version)
            {
                throw new ConflictException("concurrency_conflict", "An entry for this date already exists. Reload it before saving.");
            }

            db.SetExpectedVersion(entry, version);
        }

        entry.Edit(request.Content, request.Mood, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException ex)
        {
            throw new ConflictException("concurrency_conflict", "The entry was created elsewhere. Reload it and try again.", ex);
        }

        return await GetAsync(date, cancellationToken);
    }

    public async Task DeleteAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var owner = Owner;
        if (await db.JournalEntries.Where(j => j.OwnerId == owner && j.EntryDate == date).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            throw new NotFoundException();
        }
    }
}

public sealed class SaveJournalEntryRequestValidator : AbstractValidator<SaveJournalEntryRequest>
{
    public SaveJournalEntryRequestValidator()
    {
        RuleFor(x => x.Content).MaximumLength(JournalEntry.ContentMaxLength);
        RuleFor(x => x.Mood).InclusiveBetween(JournalEntry.MinMood, JournalEntry.MaxMood);
    }
}

public sealed class JournalQueryValidator : AbstractValidator<JournalQuery>
{
    public JournalQueryValidator()
    {
        RuleFor(x => x.Search).ValidSearch();
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        this.ValidRange(x => x.From, x => x.To, 3660);
    }
}
