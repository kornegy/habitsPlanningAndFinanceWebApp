using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalPlatform.Domain.Finance;

namespace PersonalPlatform.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("accounts");
        b.HasKey(a => a.Id);
        b.HasOwner();
        b.HasOwnerScopedKey();
        b.Property(a => a.Name).HasMaxLength(Account.NameMaxLength);
        b.Property(a => a.CurrencyCode).HasMaxLength(Currencies.CodeLength).IsFixedLength();
        b.Property(a => a.OpeningBalance).HasColumnType(ModelBuilderExtensions.MoneyType);
        // (id, owner_id, currency_code) lets ledger entries prove at the database level that their
        // currency equals the account currency.
        b.HasAlternateKey(a => new { a.Id, a.OwnerId, a.CurrencyCode });
        b.HasIndex(a => new { a.OwnerId, a.Name });
        b.HasXminVersion();
    }
}

internal sealed class FinanceCategoryConfiguration : IEntityTypeConfiguration<FinanceCategory>
{
    public void Configure(EntityTypeBuilder<FinanceCategory> b)
    {
        b.ToTable("finance_categories", t => t.HasCheckConstraint("ck_finance_categories_not_self_parent", "parent_id IS NULL OR parent_id <> id"));
        b.HasKey(c => c.Id);
        b.HasOwner();
        b.HasOwnerScopedKey();
        b.Property(c => c.Name).HasMaxLength(FinanceCategory.NameMaxLength);
        b.ReferencesOwned<FinanceCategory, FinanceCategory>(nameof(FinanceCategory.ParentId));
        b.HasIndex(c => new { c.OwnerId, c.Kind, c.ParentId, c.Name }).IsUnique().AreNullsDistinct(false);
        b.HasXminVersion();
    }
}

internal sealed class FinancialTransactionConfiguration : IEntityTypeConfiguration<FinancialTransaction>
{
    public void Configure(EntityTypeBuilder<FinancialTransaction> b)
    {
        b.ToTable("transactions", t =>
        {
            t.HasCheckConstraint("ck_transactions_amount_positive", "amount > 0");
            t.HasCheckConstraint("ck_transactions_destination_amount_positive", "destination_amount IS NULL OR destination_amount > 0");
            t.HasCheckConstraint(
                "ck_transactions_transfer_shape",
                "(kind = 2 AND destination_account_id IS NOT NULL AND destination_amount IS NOT NULL AND category_id IS NULL AND destination_account_id <> account_id)"
                + " OR (kind <> 2 AND destination_account_id IS NULL AND destination_amount IS NULL)");
        });
        b.HasKey(t => t.Id);
        b.HasOwner();
        b.HasOwnerScopedKey();
        b.Property(t => t.Description).HasMaxLength(FinancialTransaction.DescriptionMaxLength);
        b.Property(t => t.Amount).HasColumnType(ModelBuilderExtensions.MoneyType);
        b.Property(t => t.DestinationAmount).HasColumnType(ModelBuilderExtensions.MoneyType);
        b.Property(t => t.CurrencyCode).HasMaxLength(Currencies.CodeLength).IsFixedLength();
        b.Property(t => t.DestinationCurrencyCode).HasMaxLength(Currencies.CodeLength).IsFixedLength();
        b.Property(t => t.IdempotencyKey).HasMaxLength(FinancialTransaction.IdempotencyKeyMaxLength);
        b.Property(t => t.RequestFingerprint).HasMaxLength(64);

        b.ReferencesOwned<FinancialTransaction, Account>(nameof(FinancialTransaction.AccountId));
        b.ReferencesOwned<FinancialTransaction, Account>(nameof(FinancialTransaction.DestinationAccountId));
        b.ReferencesOwned<FinancialTransaction, FinanceCategory>(nameof(FinancialTransaction.CategoryId));
        b.ReferencesOwned<FinancialTransaction, RecurringTransaction>(nameof(FinancialTransaction.RecurringTransactionId));

        b.HasMany(t => t.Entries).WithOne().HasForeignKey(e => e.TransactionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(t => t.Entries).HasField("_entries");

        b.HasIndex(t => new { t.OwnerId, t.OccurredOn });
        b.HasIndex(t => new { t.OwnerId, t.CategoryId });
        // Retries with the same Idempotency-Key can never create a second transaction.
        b.HasIndex(t => new { t.OwnerId, t.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        // A recurring occurrence is posted at most once.
        b.HasIndex(t => new { t.RecurringTransactionId, t.RecurrenceDate }).IsUnique().HasFilter("recurring_transaction_id IS NOT NULL");
        b.HasXminVersion();
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.ToTable("ledger_entries", t => t.HasCheckConstraint("ck_ledger_entries_amount_nonzero", "amount <> 0"));
        b.HasKey(e => e.Id);
        b.HasOwner();
        b.Property(e => e.Amount).HasColumnType(ModelBuilderExtensions.MoneyType);
        b.Property(e => e.CurrencyCode).HasMaxLength(Currencies.CodeLength).IsFixedLength();
        // Entry must reference an account of the same owner AND in the same currency.
        b.HasOne<Account>()
            .WithMany()
            .HasForeignKey(e => new { e.AccountId, e.OwnerId, e.CurrencyCode })
            .HasPrincipalKey(a => new { a.Id, a.OwnerId, a.CurrencyCode })
            .OnDelete(DeleteBehavior.NoAction);
        // Balance and period queries: SUM(amount) per account (optionally up to a date).
        b.HasIndex(e => new { e.AccountId, e.OccurredOn }).IncludeProperties(e => e.Amount);
        b.HasIndex(e => new { e.OwnerId, e.OccurredOn });
        b.HasIndex(e => e.TransactionId);
    }
}

internal sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> b)
    {
        b.ToTable("budgets", t =>
        {
            t.HasCheckConstraint("ck_budgets_limit_positive", "monthly_limit > 0");
            t.HasCheckConstraint("ck_budgets_threshold", "warning_threshold_percent BETWEEN 1 AND 100");
        });
        b.HasKey(x => x.Id);
        b.HasOwner();
        b.Property(x => x.MonthlyLimit).HasColumnType(ModelBuilderExtensions.MoneyType);
        b.Property(x => x.CurrencyCode).HasMaxLength(Currencies.CodeLength).IsFixedLength();
        b.ReferencesOwned<Budget, FinanceCategory>(nameof(Budget.CategoryId), DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.OwnerId, x.CategoryId, x.CurrencyCode }).IsUnique().AreNullsDistinct(false);
        b.HasXminVersion();
    }
}

internal sealed class RecurringTransactionConfiguration : IEntityTypeConfiguration<RecurringTransaction>
{
    public void Configure(EntityTypeBuilder<RecurringTransaction> b)
    {
        b.ToTable("recurring_transactions", t =>
        {
            t.HasCheckConstraint("ck_recurring_amount_positive", "amount > 0");
            t.HasCheckConstraint("ck_recurring_interval", "\"interval\" BETWEEN 1 AND 365");
        });
        b.HasKey(r => r.Id);
        b.HasOwner();
        b.HasOwnerScopedKey();
        b.Ignore(r => r.Pattern);
        b.Property(r => r.Amount).HasColumnType(ModelBuilderExtensions.MoneyType);
        b.Property(r => r.DestinationAmount).HasColumnType(ModelBuilderExtensions.MoneyType);
        b.Property(r => r.Description).HasMaxLength(FinancialTransaction.DescriptionMaxLength);
        b.ReferencesOwned<RecurringTransaction, Account>(nameof(RecurringTransaction.AccountId));
        b.ReferencesOwned<RecurringTransaction, Account>(nameof(RecurringTransaction.DestinationAccountId));
        b.ReferencesOwned<RecurringTransaction, FinanceCategory>(nameof(RecurringTransaction.CategoryId));
        b.HasIndex(r => new { r.OwnerId, r.NextOccurrence });
        b.HasXminVersion();
    }
}
