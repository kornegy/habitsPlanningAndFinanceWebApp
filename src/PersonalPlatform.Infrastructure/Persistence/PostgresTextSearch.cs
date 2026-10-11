using System.Text;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Domain.Notes;
using PersonalPlatform.Infrastructure.Persistence.Configurations;

namespace PersonalPlatform.Infrastructure.Persistence;

/// <summary>
/// Full-text search over the stored <c>tsvector</c> columns. User input never reaches the tsquery parser as
/// syntax: it is reduced to letter/digit words, each turned into a prefix term, and passed as a bound
/// parameter.
/// </summary>
public sealed class PostgresTextSearch : ITextSearch
{
    private const int MaxTerms = 8;
    private const int MaxTermLength = 40;

    public IQueryable<Note> MatchNotes(IQueryable<Note> query, string search)
    {
        var tsQuery = BuildPrefixQuery(search);
        return tsQuery is null
            ? query.Where(_ => false)
            : query.Where(n => EF.Property<NpgsqlTsVector>(n, SearchColumns.SearchVector).Matches(EF.Functions.ToTsQuery("simple", tsQuery)));
    }

    public IQueryable<JournalEntry> MatchJournal(IQueryable<JournalEntry> query, string search)
    {
        var tsQuery = BuildPrefixQuery(search);
        return tsQuery is null
            ? query.Where(_ => false)
            : query.Where(j => EF.Property<NpgsqlTsVector>(j, SearchColumns.SearchVector).Matches(EF.Functions.ToTsQuery("simple", tsQuery)));
    }

    /// <summary>"Купить молоко" → "купить:* &amp; молоко:*"; returns null when no searchable word remains.</summary>
    internal static string? BuildPrefixQuery(string search)
    {
        var terms = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in search)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (current.Length < MaxTermLength)
                {
                    current.Append(char.ToLowerInvariant(ch));
                }
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return terms.Count == 0 ? null : string.Join(" & ", terms.Take(MaxTerms).Select(t => t + ":*"));

        void Flush()
        {
            if (current.Length > 0)
            {
                terms.Add(current.ToString());
                current.Clear();
            }
        }
    }
}
