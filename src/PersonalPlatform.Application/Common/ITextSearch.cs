using PersonalPlatform.Domain.Notes;

namespace PersonalPlatform.Application.Common;

/// <summary>Database full-text search (prefix matching on every word of the user's input).</summary>
public interface ITextSearch
{
    IQueryable<Note> MatchNotes(IQueryable<Note> query, string search);

    IQueryable<JournalEntry> MatchJournal(IQueryable<JournalEntry> query, string search);
}
