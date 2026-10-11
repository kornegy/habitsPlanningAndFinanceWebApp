using CsCheck;
using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Finance;
using PersonalPlatform.Domain.Scheduling;

namespace PersonalPlatform.UnitTests.Domain;

public sealed class FinanceTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 5, 10);
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Theory]
    [InlineData(10.00, "RUB", true)]
    [InlineData(10.01, "EUR", true)]
    [InlineData(10.005, "EUR", false)]
    [InlineData(100, "JPY", true)]
    [InlineData(100.5, "JPY", false)]
    [InlineData(1.234, "KWD", true)]
    [InlineData(1.2345, "KWD", false)]
    public void Precision_follows_the_currency_minor_units(decimal amount, string currency, bool valid) =>
        Assert.Equal(valid, Currencies.HasValidPrecision(amount, currency));

    [Theory]
    [InlineData("rub")]
    [InlineData("XXX")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("RUBB")]
    public void Unknown_currency_codes_are_not_supported(string? code) => Assert.False(Currencies.IsSupported(code));

    [Fact]
    public void Expense_and_income_produce_one_signed_entry()
    {
        var account = Account("RUB");
        var expense = FinancialTransaction.Create(Owner, new TransactionDetails(TransactionKind.Expense, Day, "x", account, 150.25m), Now);
        var income = FinancialTransaction.Create(Owner, new TransactionDetails(TransactionKind.Income, Day, "y", account, 99m), Now);

        Assert.Equal(-150.25m, Assert.Single(expense.Entries).Amount);
        Assert.Equal(99m, Assert.Single(income.Entries).Amount);
        Assert.All(expense.Entries.Concat(income.Entries), e => Assert.Equal(account.Id, e.AccountId));
    }

    [Fact]
    public void Same_currency_transfer_nets_to_zero()
    {
        var from = Account("EUR");
        var to = Account("EUR");

        var transfer = FinancialTransaction.Create(Owner, new TransactionDetails(TransactionKind.Transfer, Day, null, from, 42.42m, Destination: to), Now);

        Assert.Equal(2, transfer.Entries.Count);
        Assert.Equal(0m, transfer.Entries.Sum(e => e.Amount));
        Assert.Equal(42.42m, transfer.DestinationAmount);
    }

    [Fact]
    public void Cross_currency_transfer_requires_explicit_amount_and_keeps_both_currencies()
    {
        var rub = Account("RUB");
        var usd = Account("USD");

        Assert.Equal("destination_amount_required", Assert.Throws<DomainRuleException>(() =>
            FinancialTransaction.Create(Owner, new TransactionDetails(TransactionKind.Transfer, Day, null, rub, 9000m, Destination: usd), Now)).Code);

        var transfer = FinancialTransaction.Create(Owner, new TransactionDetails(TransactionKind.Transfer, Day, null, rub, 9000m, Destination: usd, DestinationAmount: 100m), Now);
        Assert.Equal(-9000m, transfer.Entries.Single(e => e.CurrencyCode == "RUB").Amount);
        Assert.Equal(100m, transfer.Entries.Single(e => e.CurrencyCode == "USD").Amount);
    }

    [Fact]
    public void Foreign_accounts_categories_and_archived_accounts_are_rejected()
    {
        var foreign = new Account(Guid.NewGuid(), "Other", AccountType.Cash, "RUB", 0m, Day, Now);
        var archived = Account("RUB");
        archived.Archive(Now);
        var foreignCategory = new FinanceCategory(Guid.NewGuid(), "Food", CategoryKind.Expense, null, Now);
        var incomeCategory = new FinanceCategory(Owner, "Salary", CategoryKind.Income, null, Now);

        Assert.Equal("account_not_found", Code(() => Create(TransactionKind.Expense, foreign, 1m)));
        Assert.Equal("account_archived", Code(() => Create(TransactionKind.Expense, archived, 1m)));
        Assert.Equal("category_not_found", Code(() => FinancialTransaction.Create(Owner, new TransactionDetails(TransactionKind.Expense, Day, null, Account("RUB"), 1m, foreignCategory), Now)));
        Assert.Equal("category_kind_mismatch", Code(() => FinancialTransaction.Create(Owner, new TransactionDetails(TransactionKind.Expense, Day, null, Account("RUB"), 1m, incomeCategory), Now)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_000_000_001)]
    public void Non_positive_or_out_of_range_amounts_are_rejected(decimal amount) =>
        Assert.Throws<DomainRuleException>(() => Create(TransactionKind.Expense, Account("RUB"), amount));

    [Fact]
    public void Updating_rebuilds_the_ledger_and_forbids_kind_changes()
    {
        var a = Account("RUB");
        var b = Account("RUB");
        var transaction = Create(TransactionKind.Expense, a, 10m);
        var oldEntryIds = transaction.Entries.Select(e => e.Id).ToList();

        transaction.Update(new TransactionDetails(TransactionKind.Expense, Day.AddDays(1), "moved", b, 25m), Now);

        var entry = Assert.Single(transaction.Entries);
        Assert.Equal(b.Id, entry.AccountId);
        Assert.Equal(-25m, entry.Amount);
        Assert.Equal(Day.AddDays(1), entry.OccurredOn);
        Assert.DoesNotContain(entry.Id, oldEntryIds);
        Assert.Equal("kind_change_not_allowed", Code(() => transaction.Update(new TransactionDetails(TransactionKind.Income, Day, null, a, 1m), Now)));
    }

    [Fact]
    public void Property_any_sequence_of_same_currency_operations_keeps_exact_totals()
    {
        // Amounts in kopecks to model real 2-decimal input; decimal arithmetic must stay exact.
        var gen = Gen.Select(Gen.Int[0, 2], Gen.Long[1, 10_000_000_00L]).Array[1, 60];

        gen.Sample(ops =>
        {
            var a = Account("RUB");
            var b = Account("RUB");
            decimal expected = 0;
            var entries = new List<LedgerEntry>();
            foreach (var (kind, kopecks) in ops)
            {
                var amount = kopecks / 100m;
                var t = (TransactionKind)kind switch
                {
                    TransactionKind.Income => Create(TransactionKind.Income, a, amount),
                    TransactionKind.Expense => Create(TransactionKind.Expense, a, amount),
                    _ => FinancialTransaction.Create(Owner, new TransactionDetails(TransactionKind.Transfer, Day, null, a, amount, Destination: b), Now),
                };
                expected += (TransactionKind)kind switch
                {
                    TransactionKind.Income => amount,
                    TransactionKind.Expense => -amount,
                    _ => 0m,
                };
                entries.AddRange(t.Entries);
            }

            // Total capital (both accounts) changes only by income minus expense; transfers net out exactly.
            Assert.Equal(expected, entries.Sum(e => e.Amount));
        }, iter: 300);
    }

    [Theory]
    [InlineData(500, 80, BudgetState.Ok, 50.0)]
    [InlineData(800, 80, BudgetState.Warning, 80.0)]
    [InlineData(1000, 80, BudgetState.Warning, 100.0)]
    [InlineData(1000.01, 80, BudgetState.Exceeded, 100.0)]
    [InlineData(333.335, 90, BudgetState.Ok, 33.3)]
    [InlineData(333.35, 90, BudgetState.Ok, 33.3)]
    [InlineData(334.5, 90, BudgetState.Ok, 33.5)]
    public void Budget_status_thresholds_and_rounding(decimal spent, int threshold, BudgetState state, decimal percent)
    {
        var status = BudgetStatus.Calculate(1000m, threshold, spent);

        Assert.Equal(state, status.State);
        Assert.Equal(percent, status.UsedPercent);
        Assert.Equal(1000m - spent, status.Remaining);
    }

    [Fact]
    public void Budgets_only_for_own_expense_categories()
    {
        var income = new FinanceCategory(Owner, "Salary", CategoryKind.Income, null, Now);
        var foreign = new FinanceCategory(Guid.NewGuid(), "Food", CategoryKind.Expense, null, Now);

        Assert.Equal("category_kind_mismatch", Code(() => new Budget(Owner, income, "RUB", 100m, 80, Now)));
        Assert.Equal("category_not_found", Code(() => new Budget(Owner, foreign, "RUB", 100m, 80, Now)));
        Assert.Equal("invalid_amount_precision", Code(() => new Budget(Owner, null, "JPY", 100.5m, 80, Now)));
    }

    [Fact]
    public void Categories_allow_only_two_levels_of_the_same_kind()
    {
        var parent = new FinanceCategory(Owner, "Food", CategoryKind.Expense, null, Now);
        var child = new FinanceCategory(Owner, "Cafe", CategoryKind.Expense, parent, Now);

        Assert.Equal("invalid_parent", Code(() => new FinanceCategory(Owner, "Too deep", CategoryKind.Expense, child, Now)));
        Assert.Equal("invalid_parent", Code(() => new FinanceCategory(Owner, "Wrong kind", CategoryKind.Income, parent, Now)));
    }

    [Fact]
    public void Recurring_template_posts_due_dates_and_never_backfills_after_edit_or_resume()
    {
        var account = Account("RUB");
        var sample = new TransactionDetails(TransactionKind.Expense, new DateOnly(2026, 1, 15), "Rent", account, 1000m);
        var template = new RecurringTransaction(Owner, sample, new(RecurrenceFrequency.Monthly, 1, Weekdays.None, new DateOnly(2026, 1, 15), null), Now);

        Assert.Equal([new(2026, 1, 15), new(2026, 2, 15), new(2026, 3, 15)], template.DueDates(new DateOnly(2026, 3, 20)));
        template.MarkPostedThrough(new DateOnly(2026, 3, 15), Now);
        Assert.Equal(new DateOnly(2026, 4, 15), template.NextOccurrence);

        template.Pause(Now);
        Assert.Empty(template.DueDates(new DateOnly(2026, 8, 1)));
        template.Resume(new DateOnly(2026, 8, 1), Now);
        Assert.Equal(new DateOnly(2026, 8, 15), template.NextOccurrence);
    }

    private static Account Account(string currency) => new(Owner, "Account", AccountType.Checking, currency, 0m, Day, Now);

    private static FinancialTransaction Create(TransactionKind kind, Account account, decimal amount) =>
        FinancialTransaction.Create(Owner, new TransactionDetails(kind, Day, null, account, amount), Now);

    private static string Code(Action action) => Assert.Throws<DomainRuleException>(action).Code;

    private static string Code(Func<object> action) => Assert.Throws<DomainRuleException>(() => action()).Code;
}
