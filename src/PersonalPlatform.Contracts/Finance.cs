using PersonalPlatform.Contracts.Tasks;

namespace PersonalPlatform.Contracts.Finance;

public enum AccountType
{
    Cash = 0,
    Checking = 1,
    Savings = 2,
    CreditCard = 3,
    Investment = 4,
    Other = 5,
}

public enum CategoryKind
{
    Expense = 0,
    Income = 1,
}

public enum TransactionKind
{
    Expense = 0,
    Income = 1,
    Transfer = 2,
}

public enum BudgetState
{
    Ok = 0,
    Warning = 1,
    Exceeded = 2,
}

public enum TransactionSort
{
    DateDesc = 0,
    DateAsc = 1,
    AmountDesc = 2,
    AmountAsc = 3,
}

/// <param name="Balance">Server-computed: opening balance plus all ledger entries. Never accepted from clients.</param>
public sealed record AccountDto(
    Guid Id,
    string Name,
    AccountType Type,
    string CurrencyCode,
    decimal OpeningBalance,
    DateOnly OpeningDate,
    decimal Balance,
    bool IsArchived,
    uint Version);

public sealed record AccountsOverviewDto(IReadOnlyList<AccountDto> Accounts, IReadOnlyList<CurrencyAmount> TotalsByCurrency);

public sealed record CreateAccountRequest(string Name, AccountType Type, string CurrencyCode, decimal OpeningBalance, DateOnly? OpeningDate);

public sealed record UpdateAccountRequest(string Name, AccountType Type, decimal OpeningBalance, DateOnly OpeningDate, uint Version);

public sealed record FinanceCategoryDto(Guid Id, string Name, CategoryKind Kind, Guid? ParentId, bool IsArchived, uint Version);

public sealed record CreateFinanceCategoryRequest(string Name, CategoryKind Kind, Guid? ParentId);

public sealed record UpdateFinanceCategoryRequest(string Name, Guid? ParentId, bool IsArchived, uint Version);

public sealed record TransactionDto(
    Guid Id,
    TransactionKind Kind,
    DateOnly OccurredOn,
    string? Description,
    Guid? CategoryId,
    string? CategoryName,
    Guid AccountId,
    string AccountName,
    decimal Amount,
    string CurrencyCode,
    Guid? DestinationAccountId,
    string? DestinationAccountName,
    decimal? DestinationAmount,
    string? DestinationCurrencyCode,
    Guid? RecurringTransactionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version);

/// <summary>
/// Creates an income, expense or transfer. Send an <c>Idempotency-Key</c> header (unique per user intent) so
/// that retries never create a duplicate.
/// </summary>
public sealed record CreateTransactionRequest(
    TransactionKind Kind,
    DateOnly OccurredOn,
    string? Description,
    Guid AccountId,
    decimal Amount,
    Guid? CategoryId,
    Guid? DestinationAccountId,
    decimal? DestinationAmount);

public sealed record UpdateTransactionRequest(
    DateOnly OccurredOn,
    string? Description,
    Guid AccountId,
    decimal Amount,
    Guid? CategoryId,
    Guid? DestinationAccountId,
    decimal? DestinationAmount,
    uint Version);

public sealed record TransactionQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? AccountId = null,
    Guid? CategoryId = null,
    TransactionKind? Kind = null,
    string? Search = null,
    TransactionSort Sort = TransactionSort.DateDesc,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

public sealed record BudgetDto(Guid Id, Guid? CategoryId, string? CategoryName, string CurrencyCode, decimal MonthlyLimit, int WarningThresholdPercent, uint Version);

public sealed record CreateBudgetRequest(Guid? CategoryId, string CurrencyCode, decimal MonthlyLimit, int WarningThresholdPercent);

public sealed record UpdateBudgetRequest(decimal MonthlyLimit, int WarningThresholdPercent, uint Version);

public sealed record BudgetUsageDto(
    Guid BudgetId,
    Guid? CategoryId,
    string? CategoryName,
    string CurrencyCode,
    decimal Limit,
    decimal Spent,
    decimal Remaining,
    decimal UsedPercent,
    BudgetState State);

public sealed record BudgetReportDto(int Year, int Month, IReadOnlyList<BudgetUsageDto> Items);

public sealed record RecurringTransactionDto(
    Guid Id,
    TransactionKind Kind,
    Guid AccountId,
    string AccountName,
    Guid? DestinationAccountId,
    decimal? DestinationAmount,
    Guid? CategoryId,
    decimal Amount,
    string CurrencyCode,
    string? Description,
    RecurrenceDto Schedule,
    DateOnly? NextOccurrence,
    bool IsPaused,
    uint Version);

public sealed record SaveRecurringTransactionRequest(
    TransactionKind Kind,
    Guid AccountId,
    decimal Amount,
    Guid? CategoryId,
    Guid? DestinationAccountId,
    decimal? DestinationAmount,
    string? Description,
    RecurrenceDto Schedule,
    uint? Version);

public sealed record SetPausedRequest(bool IsPaused);

public sealed record PostDueResult(int Posted);

public sealed record MonthAmountDto(int Month, decimal Income, decimal Expense, decimal Net);

public sealed record CurrencyYearSummaryDto(string CurrencyCode, IReadOnlyList<MonthAmountDto> Months, decimal TotalIncome, decimal TotalExpense, decimal Net);

public sealed record YearSummaryDto(int Year, IReadOnlyList<CurrencyYearSummaryDto> Currencies);

public sealed record CategoryAmountDto(Guid? CategoryId, string Name, decimal Amount, decimal Percent);

public sealed record CategoryBreakdownDto(DateOnly From, DateOnly To, string CurrencyCode, CategoryKind Kind, decimal Total, IReadOnlyList<CategoryAmountDto> Items);

/// <summary>Income, expense and net cash flow per currency for a period. Transfers are excluded.</summary>
public sealed record PeriodSummaryDto(DateOnly From, DateOnly To, IReadOnlyList<CurrencyFlowDto> Currencies);

public sealed record CurrencyFlowDto(string CurrencyCode, decimal Income, decimal Expense, decimal Net);
