using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Finance;
using DomainKind = PersonalPlatform.Domain.Finance.TransactionKind;

namespace PersonalPlatform.Application.Finance;

/// <summary>
/// Aggregated reports. Amounts are summed exactly per currency and never combined across currencies.
/// Transfers between the owner's own accounts are excluded from income/expense figures.
/// </summary>
public sealed class FinanceReportService(IAppDbContext db, ICurrentUser currentUser)
{
    private Guid Owner => currentUser.UserId;

    public async Task<YearSummaryDto> YearSummaryAsync(int year, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.OwnerId == owner && t.Kind != DomainKind.Transfer && t.OccurredOn >= start && t.OccurredOn <= end)
            .GroupBy(t => new { t.CurrencyCode, t.OccurredOn.Month, t.Kind })
            .Select(g => new { g.Key.CurrencyCode, g.Key.Month, g.Key.Kind, Total = g.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);

        var currencies = rows.GroupBy(r => r.CurrencyCode).OrderBy(g => g.Key, StringComparer.Ordinal).Select(byCurrency =>
        {
            var months = Enumerable.Range(1, 12).Select(month =>
            {
                var income = byCurrency.Where(r => r.Month == month && r.Kind == DomainKind.Income).Sum(r => r.Total);
                var expense = byCurrency.Where(r => r.Month == month && r.Kind == DomainKind.Expense).Sum(r => r.Total);
                return new MonthAmountDto(month, income, expense, income - expense);
            }).ToList();
            var totalIncome = months.Sum(m => m.Income);
            var totalExpense = months.Sum(m => m.Expense);
            return new CurrencyYearSummaryDto(byCurrency.Key, months, totalIncome, totalExpense, totalIncome - totalExpense);
        }).ToList();

        return new YearSummaryDto(year, currencies);
    }

    public async Task<PeriodSummaryDto> PeriodSummaryAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.OwnerId == owner && t.Kind != DomainKind.Transfer && t.OccurredOn >= from && t.OccurredOn <= to)
            .GroupBy(t => new { t.CurrencyCode, t.Kind })
            .Select(g => new { g.Key.CurrencyCode, g.Key.Kind, Total = g.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);

        var flows = rows.GroupBy(r => r.CurrencyCode).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var income = g.Where(r => r.Kind == DomainKind.Income).Sum(r => r.Total);
            var expense = g.Where(r => r.Kind == DomainKind.Expense).Sum(r => r.Total);
            return new CurrencyFlowDto(g.Key, income, expense, income - expense);
        }).ToList();
        return new PeriodSummaryDto(from, to, flows);
    }

    /// <summary>Totals per top-level category (subcategories roll up into their parent).</summary>
    public async Task<CategoryBreakdownDto> CategoryBreakdownAsync(CategoryBreakdownQuery query, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var kind = query.Kind == Contracts.Finance.CategoryKind.Income ? DomainKind.Income : DomainKind.Expense;
        var categories = await db.FinanceCategories.AsNoTracking()
            .Where(c => c.OwnerId == owner)
            .Select(c => new { c.Id, c.ParentId, c.Name })
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var totals = await db.Transactions.AsNoTracking()
            .Where(t => t.OwnerId == owner && t.Kind == kind && t.CurrencyCode == query.CurrencyCode && t.OccurredOn >= query.From && t.OccurredOn <= query.To)
            .GroupBy(t => t.CategoryId)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);

        var rolledUp = totals
            .GroupBy(t => t.CategoryId is { } id && categories.TryGetValue(id, out var c) ? c.ParentId ?? c.Id : (Guid?)null)
            .Select(g => (CategoryId: g.Key, Amount: g.Sum(x => x.Total)))
            .ToList();
        var total = rolledUp.Sum(x => x.Amount);
        var items = rolledUp
            .OrderByDescending(x => x.Amount)
            .Select(x => new CategoryAmountDto(
                x.CategoryId,
                x.CategoryId is { } id ? categories[id].Name : string.Empty,
                x.Amount,
                total == 0 ? 0 : decimal.Round(x.Amount / total * 100m, 1, MidpointRounding.AwayFromZero)))
            .ToList();
        return new CategoryBreakdownDto(query.From, query.To, query.CurrencyCode, query.Kind, total, items);
    }
}

public sealed record DateRangeQuery(DateOnly From, DateOnly To);

public sealed class DateRangeQueryValidator : AbstractValidator<DateRangeQuery>
{
    public DateRangeQueryValidator() => this.ValidRange(x => x.From, x => x.To, QueryLimits.MaxRangeDays);
}

public sealed record CategoryBreakdownQuery(DateOnly From, DateOnly To, string CurrencyCode, Contracts.Finance.CategoryKind Kind = Contracts.Finance.CategoryKind.Expense);

public sealed class CategoryBreakdownQueryValidator : AbstractValidator<CategoryBreakdownQuery>
{
    public CategoryBreakdownQueryValidator()
    {
        RuleFor(x => x.CurrencyCode).ValidCurrency();
        RuleFor(x => x.Kind).IsInEnum();
        this.ValidRange(x => x.From, x => x.To, QueryLimits.MaxRangeDays);
    }
}

public sealed record YearQuery(int Year);

public sealed class YearQueryValidator : AbstractValidator<YearQuery>
{
    public YearQueryValidator() => RuleFor(x => x.Year).InclusiveBetween(ValidationRules.MinDate.Year, ValidationRules.MaxDate.Year);
}
