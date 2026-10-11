using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Domain.Finance;

/// <summary>
/// Signed change of one account's balance caused by a transaction. Created only by
/// <see cref="FinancialTransaction"/>; never edited directly.
/// </summary>
public sealed class LedgerEntry : IOwnedEntity
{
    private LedgerEntry()
    {
    }

    internal LedgerEntry(FinancialTransaction transaction, Guid accountId, decimal amount, string currencyCode)
    {
        Id = Guid.CreateVersion7();
        OwnerId = transaction.OwnerId;
        TransactionId = transaction.Id;
        AccountId = accountId;
        Amount = amount;
        CurrencyCode = currencyCode;
        OccurredOn = transaction.OccurredOn;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid TransactionId { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>Signed amount: negative decreases the account balance, positive increases it.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    /// <summary>Copy of the transaction date so balance-at-date and period queries need no join.</summary>
    public DateOnly OccurredOn { get; private set; }
}
