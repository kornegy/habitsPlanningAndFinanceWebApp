using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Domain.Finance;

public enum TransactionKind
{
    Expense = 0,
    Income = 1,
    Transfer = 2,
}

/// <summary>Validated input for creating or changing a financial transaction.</summary>
/// <param name="Source">The account money leaves (expense, transfer) or enters (income).</param>
/// <param name="Destination">Transfer target account; null for income/expense.</param>
/// <param name="DestinationAmount">
/// Amount credited to <paramref name="Destination"/>. Required only when the currencies differ (an explicit,
/// user-provided conversion); for same-currency transfers it must be null or equal to <paramref name="Amount"/>.
/// </param>
public sealed record TransactionDetails(
    TransactionKind Kind,
    DateOnly OccurredOn,
    string? Description,
    Account Source,
    decimal Amount,
    FinanceCategory? Category = null,
    Account? Destination = null,
    decimal? DestinationAmount = null);

/// <summary>
/// A user-visible money movement. Balances are derived only from its <see cref="Entries"/> (ledger), which
/// are always rebuilt by this class from validated details, never supplied by clients.
/// </summary>
/// <remarks>
/// Ledger rules: an expense produces one negative entry, an income one positive entry, a transfer one
/// negative entry on the source and one positive entry on the destination. A same-currency transfer therefore
/// sums to exactly zero, so transfers never change a user's total capital in that currency.
/// </remarks>
public sealed class FinancialTransaction : IOwnedEntity
{
    public const int DescriptionMaxLength = 300;
    public const int IdempotencyKeyMaxLength = 64;

    private readonly List<LedgerEntry> _entries = [];

    private FinancialTransaction()
    {
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public TransactionKind Kind { get; private set; }

    /// <summary>Calendar date of the operation as the user sees it (not the creation time).</summary>
    public DateOnly OccurredOn { get; private set; }

    public string? Description { get; private set; }

    public Guid? CategoryId { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>Positive amount in the source account currency.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public Guid? DestinationAccountId { get; private set; }

    public decimal? DestinationAmount { get; private set; }

    public string? DestinationCurrencyCode { get; private set; }

    public Guid? RecurringTransactionId { get; private set; }

    /// <summary>Scheduled date this transaction was generated for (recurring transactions only).</summary>
    public DateOnly? RecurrenceDate { get; private set; }

    public string? IdempotencyKey { get; private set; }

    /// <summary>Hash of the original request, used to reject reuse of an idempotency key with a different body.</summary>
    public string? RequestFingerprint { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    public static FinancialTransaction Create(Guid ownerId, TransactionDetails details, DateTimeOffset now)
    {
        var transaction = new FinancialTransaction
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            CreatedAt = now,
        };
        transaction.Apply(details, now);
        return transaction;
    }

    public void AttachIdempotency(string key, string fingerprint)
    {
        Rule.Require(key.Length is > 0 and <= IdempotencyKeyMaxLength, "invalid_idempotency_key", "Invalid idempotency key.");
        IdempotencyKey = key;
        RequestFingerprint = fingerprint;
    }

    public void AttachRecurrence(Guid recurringTransactionId, DateOnly occurrenceDate)
    {
        RecurringTransactionId = recurringTransactionId;
        RecurrenceDate = occurrenceDate;
    }

    public void Update(TransactionDetails details, DateTimeOffset now)
    {
        Rule.Require(details.Kind == Kind, "kind_change_not_allowed", "Transaction kind cannot be changed; delete and recreate it instead.");
        Apply(details, now);
    }

    private void Apply(TransactionDetails details, DateTimeOffset now)
    {
        Validate(details);

        Kind = details.Kind;
        OccurredOn = details.OccurredOn;
        Description = Rule.OptionalText(details.Description, DescriptionMaxLength, "invalid_description", "Description");
        CategoryId = details.Category?.Id;
        AccountId = details.Source.Id;
        Amount = details.Amount;
        CurrencyCode = details.Source.CurrencyCode;
        DestinationAccountId = details.Destination?.Id;
        DestinationCurrencyCode = details.Destination?.CurrencyCode;
        DestinationAmount = details.Destination is null ? null : details.DestinationAmount ?? details.Amount;
        UpdatedAt = now;

        RebuildEntries();
    }

    private void Validate(TransactionDetails d)
    {
        Rule.Require(Enum.IsDefined(d.Kind), "invalid_kind", "Unknown transaction kind.");
        RequireUsableAccount(d.Source);
        RequirePositiveAmount(d.Amount, d.Source.CurrencyCode);

        switch (d.Kind)
        {
            case TransactionKind.Expense or TransactionKind.Income:
                Rule.Require(d.Destination is null && d.DestinationAmount is null, "invalid_transfer_fields", "Only transfers have a destination account.");
                if (d.Category is not null)
                {
                    Rule.Require(d.Category.OwnerId == OwnerId, "category_not_found", "Category not found.");
                    var expectedKind = d.Kind == TransactionKind.Expense ? CategoryKind.Expense : CategoryKind.Income;
                    Rule.Require(d.Category.Kind == expectedKind, "category_kind_mismatch", "Category kind does not match the transaction kind.");
                }

                break;

            case TransactionKind.Transfer:
                Rule.Require(d.Category is null, "invalid_category", "Transfers do not have a category.");
                Rule.Require(d.Destination is not null, "destination_required", "Transfer requires a destination account.");
                RequireUsableAccount(d.Destination!);
                Rule.Require(d.Destination!.Id != d.Source.Id, "same_account_transfer", "Source and destination accounts must differ.");

                if (d.Destination.CurrencyCode == d.Source.CurrencyCode)
                {
                    Rule.Require(d.DestinationAmount is null || d.DestinationAmount == d.Amount, "destination_amount_mismatch", "Same-currency transfers must credit exactly the debited amount.");
                }
                else
                {
                    // Cross-currency: the user must state the credited amount explicitly; no implicit rates.
                    Rule.Require(d.DestinationAmount is not null, "destination_amount_required", "Cross-currency transfers require the credited amount.");
                    RequirePositiveAmount(d.DestinationAmount!.Value, d.Destination.CurrencyCode);
                }

                break;
        }
    }

    private void RequireUsableAccount(Account account)
    {
        Rule.Require(account.OwnerId == OwnerId, "account_not_found", "Account not found.");
        Rule.Require(!account.IsArchived, "account_archived", "Archived accounts cannot be used for new operations.");
    }

    private static void RequirePositiveAmount(decimal amount, string currency)
    {
        Rule.Require(amount > 0, "amount_not_positive", "Amount must be greater than zero.");
        Rule.Require(Currencies.IsWithinRange(amount), "amount_out_of_range", "Amount is out of range.");
        Rule.Require(Currencies.HasValidPrecision(amount, currency), "invalid_amount_precision", "Amount has too many decimal places for the currency.");
    }

    private void RebuildEntries()
    {
        _entries.Clear();
        switch (Kind)
        {
            case TransactionKind.Expense:
                _entries.Add(new LedgerEntry(this, AccountId, -Amount, CurrencyCode));
                break;
            case TransactionKind.Income:
                _entries.Add(new LedgerEntry(this, AccountId, Amount, CurrencyCode));
                break;
            case TransactionKind.Transfer:
                _entries.Add(new LedgerEntry(this, AccountId, -Amount, CurrencyCode));
                _entries.Add(new LedgerEntry(this, DestinationAccountId!.Value, DestinationAmount!.Value, DestinationCurrencyCode!));
                break;
        }
    }
}
