using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Domain.Finance;

public enum AccountType
{
    Cash = 0,
    Checking = 1,
    Savings = 2,
    CreditCard = 3,
    Investment = 4,
    Other = 5,
}

/// <summary>
/// A user's money account. The balance is never stored: it is always
/// <c>OpeningBalance + SUM(ledger_entries.amount)</c> so it cannot drift from the recorded operations.
/// </summary>
public sealed class Account : IOwnedEntity
{
    public const int NameMaxLength = 100;

    private Account()
    {
    }

    public Account(Guid ownerId, string name, AccountType type, string currencyCode, decimal openingBalance, DateOnly openingDate, DateTimeOffset now)
    {
        Rule.Require(Currencies.IsSupported(currencyCode), "invalid_currency", "Unsupported currency.");
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        CurrencyCode = currencyCode;
        CreatedAt = now;
        Update(name, type, openingBalance, openingDate, now);
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public AccountType Type { get; private set; }

    /// <summary>Immutable after creation: existing ledger entries are denominated in this currency.</summary>
    public string CurrencyCode { get; private set; } = string.Empty;

    public decimal OpeningBalance { get; private set; }

    public DateOnly OpeningDate { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public void Update(string name, AccountType type, decimal openingBalance, DateOnly openingDate, DateTimeOffset now)
    {
        Rule.Require(Enum.IsDefined(type), "invalid_account_type", "Unknown account type.");
        Rule.Require(Currencies.IsWithinRange(openingBalance), "amount_out_of_range", "Opening balance is out of range.");
        Rule.Require(Currencies.HasValidPrecision(openingBalance, CurrencyCode), "invalid_amount_precision", "Opening balance has too many decimal places for the currency.");

        Name = Rule.RequireText(name, NameMaxLength, "invalid_name", "Name");
        Type = type;
        OpeningBalance = openingBalance;
        OpeningDate = openingDate;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        ArchivedAt ??= now;
        UpdatedAt = now;
    }

    public void Restore(DateTimeOffset now)
    {
        ArchivedAt = null;
        UpdatedAt = now;
    }
}
