using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;

namespace PersonalPlatform.Application.Export;

/// <summary>
/// Exports of the owner's own data. Every query is owner-filtered, the date range and row count are bounded,
/// and CSV cells are neutralised against spreadsheet formula injection.
/// </summary>
public sealed class ExportService(IAppDbContext db, ICurrentUser currentUser, UserClock clock)
{
    public const int MaxTransactionRows = 50_000;
    public const int MaxNotes = 10_000;
    public const int MaxRangeDays = 3660;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<byte[]> TransactionsCsvAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;
        var accounts = db.Accounts;
        var categories = db.FinanceCategories;
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.OwnerId == owner && t.OccurredOn >= from && t.OccurredOn <= to)
            .OrderBy(t => t.OccurredOn).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Take(MaxTransactionRows + 1)
            .Select(t => new
            {
                t.OccurredOn,
                t.Kind,
                t.Amount,
                t.CurrencyCode,
                Account = accounts.Where(a => a.Id == t.AccountId && a.OwnerId == owner).Select(a => a.Name).First(),
                Destination = accounts.Where(a => a.Id == t.DestinationAccountId && a.OwnerId == owner).Select(a => a.Name).FirstOrDefault(),
                t.DestinationAmount,
                t.DestinationCurrencyCode,
                Category = categories.Where(c => c.Id == t.CategoryId && c.OwnerId == owner).Select(c => c.Name).FirstOrDefault(),
                t.Description,
            })
            .ToListAsync(cancellationToken);

        if (rows.Count > MaxTransactionRows)
        {
            throw new Domain.Common.DomainRuleException("export_too_large", $"The export exceeds {MaxTransactionRows} rows; choose a shorter period.");
        }

        var csv = new StringBuilder();
        csv.AppendLine("date,type,amount,currency,account,destination_account,destination_amount,destination_currency,category,description");
        foreach (var r in rows)
        {
            csv.AppendJoin(',',
                    r.OccurredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    r.Kind.ToString().ToLowerInvariant(),
                    Number(r.Amount),
                    r.CurrencyCode,
                    Csv.Text(r.Account),
                    Csv.Text(r.Destination),
                    r.DestinationAmount is { } d ? Number(d) : string.Empty,
                    r.DestinationCurrencyCode ?? string.Empty,
                    Csv.Text(r.Category),
                    Csv.Text(r.Description))
                .Append("\r\n");
        }

        // UTF-8 with BOM so spreadsheet applications detect the encoding of non-ASCII text.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    public async Task<byte[]> NotesJsonAsync(CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;
        var tags = db.Tags;
        var notes = await db.Notes.AsNoTracking()
            .Where(n => n.OwnerId == owner)
            .OrderBy(n => n.CreatedAt)
            .Take(MaxNotes)
            .Select(n => new
            {
                n.Title,
                n.Content,
                n.IsPinned,
                Archived = n.ArchivedAt != null,
                Tags = tags.Where(t => n.Tags.Select(x => x.TagId).Contains(t.Id)).Select(t => t.Name).ToList(),
                n.CreatedAt,
                n.UpdatedAt,
            })
            .ToListAsync(cancellationToken);
        var journal = await db.JournalEntries.AsNoTracking()
            .Where(j => j.OwnerId == owner)
            .OrderBy(j => j.EntryDate)
            .Select(j => new { j.EntryDate, j.Content, j.Mood, j.UpdatedAt })
            .ToListAsync(cancellationToken);
        return JsonSerializer.SerializeToUtf8Bytes(new { ExportedAt = clock.UtcNow, Notes = notes, Journal = journal }, JsonOptions);
    }

    private static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>CSV encoding (RFC 4180) with protection against formula injection (CWE-1236).</summary>
public static class Csv
{
    private static readonly char[] FormulaPrefixes = ['=', '+', '-', '@', '\t', '\r', '|', '%'];

    public static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // A leading apostrophe makes spreadsheet applications treat the cell as text, never as a formula.
        var safe = Array.IndexOf(FormulaPrefixes, value[0]) >= 0 ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

public sealed record ExportRangeQuery(DateOnly From, DateOnly To);

public sealed class ExportRangeQueryValidator : FluentValidation.AbstractValidator<ExportRangeQuery>
{
    public ExportRangeQueryValidator() => this.ValidRange(x => x.From, x => x.To, ExportService.MaxRangeDays);
}
