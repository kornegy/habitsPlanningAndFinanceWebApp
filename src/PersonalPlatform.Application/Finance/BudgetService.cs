using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts.Finance;
using PersonalPlatform.Domain.Finance;
using PersonalPlatform.Domain.Scheduling;
using DomainKind = PersonalPlatform.Domain.Finance.TransactionKind;

namespace PersonalPlatform.Application.Finance;

/// <summary>Monthly category budgets (spending limits) and plan-vs-actual reports.</summary>
public sealed class BudgetService(IAppDbContext db, ICurrentUser currentUser, UserClock clock)
{
    public const int MaxBudgetsPerUser = 200;

    private Guid Owner => currentUser.UserId;

    public async Task<IReadOnlyList<BudgetDto>> ListAsync(CancellationToken cancellationToken)
    {
        var owner = Owner;
        var categories = db.FinanceCategories;
        return await db.Budgets.AsNoTracking()
            .Where(b => b.OwnerId == owner)
            .OrderBy(b => b.CurrencyCode).ThenBy(b => b.CategoryId)
            .Select(b => new BudgetDto(
                b.Id,
                b.CategoryId,
                categories.Where(c => c.Id == b.CategoryId && c.OwnerId == owner).Select(c => c.Name).FirstOrDefault(),
                b.CurrencyCode,
                b.MonthlyLimit,
                b.WarningThresholdPercent,
                b.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task<BudgetDto> CreateAsync(CreateBudgetRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        if (await db.Budgets.CountAsync(b => b.OwnerId == owner, cancellationToken) >= MaxBudgetsPerUser)
        {
            throw new ConflictException("limit_reached", $"At most {MaxBudgetsPerUser} budgets are allowed.");
        }

        FinanceCategory? category = null;
        if (request.CategoryId is { } categoryId)
        {
            category = await db.FinanceCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId && c.OwnerId == owner, cancellationToken)
                ?? throw new NotFoundException("Category not found.");
        }

        var budget = new Budget(owner, category, request.CurrencyCode, request.MonthlyLimit, request.WarningThresholdPercent, clock.UtcNow);
        db.Budgets.Add(budget);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException ex)
        {
            throw new ConflictException("duplicate", "A budget for this category and currency already exists.", ex);
        }

        return new BudgetDto(budget.Id, budget.CategoryId, category?.Name, budget.CurrencyCode, budget.MonthlyLimit, budget.WarningThresholdPercent, budget.Version);
    }

    public async Task<BudgetDto> UpdateAsync(Guid id, UpdateBudgetRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var budget = await db.Budgets.FirstOrDefaultAsync(b => b.Id == id && b.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
        db.SetExpectedVersion(budget, request.Version);
        budget.Update(request.MonthlyLimit, request.WarningThresholdPercent, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return (await ListAsync(cancellationToken)).Single(b => b.Id == id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        if (await db.Budgets.Where(b => b.Id == id && b.OwnerId == owner).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            throw new NotFoundException();
        }
    }

    /// <summary>
    /// Plan vs actual for a calendar month. "Spent" is the exact sum of expense amounts in the budget's currency
    /// for the category and its subcategories (or all expenses for an overall budget). Transfers never count.
    /// </summary>
    public async Task<BudgetReportDto> ReportAsync(int year, int month, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var start = new DateOnly(year, month, 1);
        var end = CalendarMath.EndOfMonth(start);

        var budgets = await db.Budgets.AsNoTracking().Where(b => b.OwnerId == owner).ToListAsync(cancellationToken);
        var categories = await db.FinanceCategories.AsNoTracking()
            .Where(c => c.OwnerId == owner)
            .Select(c => new { c.Id, c.ParentId, c.Name })
            .ToListAsync(cancellationToken);
        var spending = await db.Transactions.AsNoTracking()
            .Where(t => t.OwnerId == owner && t.Kind == DomainKind.Expense && t.OccurredOn >= start && t.OccurredOn <= end)
            .GroupBy(t => new { t.CategoryId, t.CurrencyCode })
            .Select(g => new { g.Key.CategoryId, g.Key.CurrencyCode, Total = g.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);

        var items = budgets.Select(budget =>
        {
            var covered = budget.CategoryId is { } cid
                ? categories.Where(c => c.Id == cid || c.ParentId == cid).Select(c => (Guid?)c.Id).ToHashSet()
                : null;
            var spent = spending
                .Where(s => s.CurrencyCode == budget.CurrencyCode && (covered is null || covered.Contains(s.CategoryId)))
                .Sum(s => s.Total);
            var status = budget.Evaluate(spent);
            var name = budget.CategoryId is { } id ? categories.FirstOrDefault(c => c.Id == id)?.Name : null;
            return new BudgetUsageDto(budget.Id, budget.CategoryId, name, budget.CurrencyCode, status.Limit, status.Spent, status.Remaining, status.UsedPercent, status.State.ToContract());
        })
        .OrderByDescending(i => i.UsedPercent)
        .ToList();

        return new BudgetReportDto(year, month, items);
    }
}

public sealed class CreateBudgetRequestValidator : AbstractValidator<CreateBudgetRequest>
{
    public CreateBudgetRequestValidator()
    {
        RuleFor(x => x.CurrencyCode).ValidCurrency();
        RuleFor(x => x.MonthlyLimit).ValidAmount();
        RuleFor(x => x.WarningThresholdPercent).InclusiveBetween(Budget.MinWarningPercent, Budget.MaxWarningPercent);
    }
}

public sealed class UpdateBudgetRequestValidator : AbstractValidator<UpdateBudgetRequest>
{
    public UpdateBudgetRequestValidator()
    {
        RuleFor(x => x.MonthlyLimit).ValidAmount();
        RuleFor(x => x.WarningThresholdPercent).InclusiveBetween(Budget.MinWarningPercent, Budget.MaxWarningPercent);
    }
}

public sealed record MonthQuery(int Year, int Month);

public sealed class MonthQueryValidator : AbstractValidator<MonthQuery>
{
    public MonthQueryValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(ValidationRules.MinDate.Year, ValidationRules.MaxDate.Year);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
