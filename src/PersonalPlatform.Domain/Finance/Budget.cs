using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Domain.Finance;

/// <summary>
/// Monthly spending limit for an expense category (including its subcategories) or, when
/// <see cref="CategoryId"/> is null, for all expenses in <see cref="CurrencyCode"/>.
/// </summary>
public sealed class Budget : IOwnedEntity
{
    public const int MinWarningPercent = 1;
    public const int MaxWarningPercent = 100;

    private Budget()
    {
    }

    public Budget(Guid ownerId, FinanceCategory? category, string currencyCode, decimal monthlyLimit, int warningThresholdPercent, DateTimeOffset now)
    {
        Rule.Require(Currencies.IsSupported(currencyCode), "invalid_currency", "Unsupported currency.");
        if (category is not null)
        {
            Rule.Require(category.OwnerId == ownerId, "category_not_found", "Category not found.");
            Rule.Require(category.Kind == CategoryKind.Expense, "category_kind_mismatch", "Budgets apply to expense categories only.");
        }

        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        CategoryId = category?.Id;
        CurrencyCode = currencyCode;
        CreatedAt = now;
        Update(monthlyLimit, warningThresholdPercent, now);
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid? CategoryId { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public decimal MonthlyLimit { get; private set; }

    /// <summary>Usage percentage at which the budget is reported as "near limit".</summary>
    public int WarningThresholdPercent { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public void Update(decimal monthlyLimit, int warningThresholdPercent, DateTimeOffset now)
    {
        Rule.Require(monthlyLimit > 0, "amount_not_positive", "Limit must be greater than zero.");
        Rule.Require(Currencies.IsWithinRange(monthlyLimit), "amount_out_of_range", "Limit is out of range.");
        Rule.Require(Currencies.HasValidPrecision(monthlyLimit, CurrencyCode), "invalid_amount_precision", "Limit has too many decimal places for the currency.");
        Rule.Require(warningThresholdPercent is >= MinWarningPercent and <= MaxWarningPercent, "invalid_threshold", "Warning threshold must be between 1 and 100.");

        MonthlyLimit = monthlyLimit;
        WarningThresholdPercent = warningThresholdPercent;
        UpdatedAt = now;
    }

    public BudgetStatus Evaluate(decimal spent) => BudgetStatus.Calculate(MonthlyLimit, WarningThresholdPercent, spent);
}

public enum BudgetState
{
    Ok = 0,
    Warning = 1,
    Exceeded = 2,
}

/// <param name="Spent">Positive total of expenses in the period.</param>
/// <param name="UsedPercent">Spent / limit * 100, rounded to one decimal place (midpoint away from zero).</param>
public sealed record BudgetStatus(decimal Limit, decimal Spent, decimal Remaining, decimal UsedPercent, BudgetState State)
{
    public static BudgetStatus Calculate(decimal limit, int warningThresholdPercent, decimal spent)
    {
        var used = decimal.Round(spent / limit * 100m, 1, MidpointRounding.AwayFromZero);
        var state = spent > limit
            ? BudgetState.Exceeded
            : spent * 100m >= limit * warningThresholdPercent ? BudgetState.Warning : BudgetState.Ok;
        return new BudgetStatus(limit, spent, limit - spent, used, state);
    }
}
