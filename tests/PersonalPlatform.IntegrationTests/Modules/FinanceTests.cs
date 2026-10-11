using System.Net;
using System.Net.Http.Json;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Finance;
using PersonalPlatform.Contracts.Notes;
using PersonalPlatform.Contracts.Profile;
using PersonalPlatform.Contracts.Tasks;

namespace PersonalPlatform.IntegrationTests.Modules;

public sealed class FinanceTests(ApiTestEnvironment env)
{
    private static readonly DateOnly Day = new(2026, 9, 15);

    [Fact]
    public async Task Balances_are_derived_from_opening_balance_and_operations()
    {
        var user = await env.CreateUserAsync();
        var card = await AccountAsync(user, "Card", "RUB", 1000m);

        await TransactionAsync(user, new(TransactionKind.Income, Day, "Salary", card.Id, 5000.50m, null, null, null));
        await TransactionAsync(user, new(TransactionKind.Expense, Day, "Groceries", card.Id, 1234.56m, null, null, null));

        Assert.Equal(1000m + 5000.50m - 1234.56m, (await user.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{card.Id}")).Balance);
    }

    [Fact]
    public async Task Transfers_move_money_without_changing_total_capital()
    {
        var user = await env.CreateUserAsync();
        var card = await AccountAsync(user, "Card", "RUB", 10_000m);
        var savings = await AccountAsync(user, "Savings", "RUB", 2_500m);
        var before = await TotalAsync(user, "RUB");

        await TransactionAsync(user, new(TransactionKind.Transfer, Day, null, card.Id, 3_333.33m, null, savings.Id, null));

        Assert.Equal(before, await TotalAsync(user, "RUB"));
        Assert.Equal(10_000m - 3_333.33m, (await user.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{card.Id}")).Balance);
        Assert.Equal(2_500m + 3_333.33m, (await user.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{savings.Id}")).Balance);

        var mismatched = await user.PostAsync("/api/v1/finance/transactions", new CreateTransactionRequest(TransactionKind.Transfer, Day, null, card.Id, 10m, null, savings.Id, 11m));
        Assert.Equal("destination_amount_mismatch", await mismatched.ProblemCodeAsync());
        var self = await user.PostAsync("/api/v1/finance/transactions", new CreateTransactionRequest(TransactionKind.Transfer, Day, null, card.Id, 10m, null, card.Id, null));
        Assert.Equal("same_account_transfer", await self.ProblemCodeAsync());
    }

    [Fact]
    public async Task Cross_currency_transfer_requires_an_explicit_credited_amount()
    {
        var user = await env.CreateUserAsync();
        var rub = await AccountAsync(user, "RUB", "RUB", 100_000m);
        var eur = await AccountAsync(user, "EUR", "EUR", 0m);

        var withoutRate = await user.PostAsync("/api/v1/finance/transactions", new CreateTransactionRequest(TransactionKind.Transfer, Day, null, rub.Id, 9_500m, null, eur.Id, null));
        Assert.Equal("destination_amount_required", await withoutRate.ProblemCodeAsync());

        await TransactionAsync(user, new(TransactionKind.Transfer, Day, "exchange", rub.Id, 9_500m, null, eur.Id, 100m));
        var overview = await user.GetJsonAsync<AccountsOverviewDto>("/api/v1/finance/accounts");
        Assert.Equal(90_500m, overview.TotalsByCurrency.Single(t => t.CurrencyCode == "RUB").Amount);
        Assert.Equal(100m, overview.TotalsByCurrency.Single(t => t.CurrencyCode == "EUR").Amount);
    }

    [Fact]
    public async Task Amount_precision_follows_the_currency_and_is_never_rounded()
    {
        var user = await env.CreateUserAsync();
        var rub = await AccountAsync(user, "RUB", "RUB", 0m);
        var jpy = await AccountAsync(user, "JPY", "JPY", 0m);

        var threeDecimals = await user.PostAsync("/api/v1/finance/transactions", new CreateTransactionRequest(TransactionKind.Expense, Day, null, rub.Id, 10.005m, null, null, null));
        var yenFraction = await user.PostAsync("/api/v1/finance/transactions", new CreateTransactionRequest(TransactionKind.Expense, Day, null, jpy.Id, 100.5m, null, null, null));
        var negative = await user.PostAsync("/api/v1/finance/transactions", new CreateTransactionRequest(TransactionKind.Expense, Day, null, rub.Id, -5m, null, null, null));
        var huge = await user.PostAsync("/api/v1/finance/transactions", new CreateTransactionRequest(TransactionKind.Income, Day, null, rub.Id, 10_000_000_000_000m, null, null, null));

        Assert.Equal("invalid_amount_precision", await threeDecimals.ProblemCodeAsync());
        Assert.Equal("invalid_amount_precision", await yenFraction.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);
        Assert.Equal(0m, (await user.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{rub.Id}")).Balance);
    }

    [Fact]
    public async Task Idempotency_key_prevents_double_spending_on_retry()
    {
        var user = await env.CreateUserAsync();
        var card = await AccountAsync(user, "Card", "RUB", 1000m);
        var request = new CreateTransactionRequest(TransactionKind.Expense, Day, "Coffee", card.Id, 250m, null, null, null);
        var key = Guid.NewGuid().ToString();

        var first = await PostWithKeyAsync(user, request, key);
        var retry = await PostWithKeyAsync(user, request, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("true", retry.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal((await first.ReadJsonAsync<TransactionDto>()).Id, (await retry.ReadJsonAsync<TransactionDto>()).Id);
        Assert.Equal(750m, (await user.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{card.Id}")).Balance);

        var reusedForOther = await PostWithKeyAsync(user, request with { Amount = 999m }, key);
        Assert.Equal(HttpStatusCode.Conflict, reusedForOther.StatusCode);
        Assert.Equal("idempotency_key_reused", await reusedForOther.ProblemCodeAsync());

        var badKey = await PostWithKeyAsync(user, request, "short");
        Assert.Equal(HttpStatusCode.BadRequest, badKey.StatusCode);
    }

    [Fact]
    public async Task Concurrent_retries_with_one_key_create_exactly_one_operation()
    {
        var user = await env.CreateUserAsync();
        var card = await AccountAsync(user, "Card", "RUB", 1000m);
        var request = new CreateTransactionRequest(TransactionKind.Expense, Day, "Double click", card.Id, 100m, null, null, null);
        var key = Guid.NewGuid().ToString("N");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostWithKeyAsync(user, request, key)));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, r.StatusCode.ToString()));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(900m, (await user.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{card.Id}")).Balance);
    }

    [Fact]
    public async Task Many_concurrent_operations_keep_balances_exact()
    {
        var user = await env.CreateUserAsync();
        var a = await AccountAsync(user, "A", "RUB", 50_000m);
        var b = await AccountAsync(user, "B", "RUB", 0m);

        var operations = Enumerable.Range(0, 40).Select(i => i % 2 == 0
            ? new CreateTransactionRequest(TransactionKind.Transfer, Day, null, a.Id, 10.01m, null, b.Id, null)
            : new CreateTransactionRequest(TransactionKind.Expense, Day, null, a.Id, 1.10m, null, null, null));
        var responses = await Task.WhenAll(operations.Select(op => user.PostAsync("/api/v1/finance/transactions", op)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var balanceA = (await user.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{a.Id}")).Balance;
        var balanceB = (await user.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{b.Id}")).Balance;
        Assert.Equal(50_000m - (20 * 10.01m) - (20 * 1.10m), balanceA);
        Assert.Equal(20 * 10.01m, balanceB);
        Assert.Equal(50_000m - (20 * 1.10m), await TotalAsync(user, "RUB"));
    }

    [Fact]
    public async Task Editing_and_deleting_operations_recalculate_balances()
    {
        var user = await env.CreateUserAsync();
        var card = await AccountAsync(user, "Card", "RUB", 0m);
        var cash = await AccountAsync(user, "Cash", "RUB", 0m);
        var expense = await TransactionAsync(user, new(TransactionKind.Expense, Day, "Lunch", card.Id, 300m, null, null, null));

        var moved = await user.PutJsonAsync<UpdateTransactionRequest, TransactionDto>($"/api/v1/finance/transactions/{expense.Id}",
            new(Day.AddDays(1), "Lunch (cash)", cash.Id, 450m, null, null, null, expense.Version));
        Assert.Equal(0m, (await user.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{card.Id}")).Balance);
        Assert.Equal(-450m, (await user.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{cash.Id}")).Balance);

        var stale = await user.PutAsync($"/api/v1/finance/transactions/{expense.Id}", new UpdateTransactionRequest(Day, null, card.Id, 1m, null, null, null, expense.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        await user.DeleteAsync($"/api/v1/finance/transactions/{moved.Id}").EnsureStatusAsync(HttpStatusCode.NoContent);
        Assert.Equal(0m, (await user.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{cash.Id}")).Balance);
        await using var db = env.Database.CreateOwnerContext();
        Assert.Equal(0, db.LedgerEntries.IgnoreQueryFiltersForTests().Count(e => e.TransactionId == moved.Id));
    }

    [Fact]
    public async Task Category_kind_must_match_and_archived_accounts_are_read_only()
    {
        var user = await env.CreateUserAsync();
        var card = await AccountAsync(user, "Card", "RUB", 0m);
        var food = await user.PostJsonAsync<CreateFinanceCategoryRequest, FinanceCategoryDto>("/api/v1/finance/categories", new("Food", CategoryKind.Expense, null));

        var incomeWithExpenseCategory = await user.PostAsync("/api/v1/finance/transactions", new CreateTransactionRequest(TransactionKind.Income, Day, null, card.Id, 1m, food.Id, null, null));
        Assert.Equal("category_kind_mismatch", await incomeWithExpenseCategory.ProblemCodeAsync());

        await user.PostJsonAsync<SetArchivedRequest, AccountDto>($"/api/v1/finance/accounts/{card.Id}/archive", new(true), HttpStatusCode.OK);
        var onArchived = await user.PostAsync("/api/v1/finance/transactions", new CreateTransactionRequest(TransactionKind.Expense, Day, null, card.Id, 1m, food.Id, null, null));
        Assert.Equal("account_archived", await onArchived.ProblemCodeAsync());
    }

    [Fact]
    public async Task Accounts_with_history_cannot_be_deleted_and_category_deletion_uncategorises()
    {
        var user = await env.CreateUserAsync();
        var card = await AccountAsync(user, "Card", "RUB", 0m);
        var empty = await AccountAsync(user, "Unused", "RUB", 0m);
        var food = await user.PostJsonAsync<CreateFinanceCategoryRequest, FinanceCategoryDto>("/api/v1/finance/categories", new("Food", CategoryKind.Expense, null));
        var restaurants = await user.PostJsonAsync<CreateFinanceCategoryRequest, FinanceCategoryDto>("/api/v1/finance/categories", new("Restaurants", CategoryKind.Expense, food.Id));
        var tx = await TransactionAsync(user, new(TransactionKind.Expense, Day, null, card.Id, 10m, restaurants.Id, null, null));

        Assert.Equal("account_in_use", await (await user.DeleteAsync($"/api/v1/finance/accounts/{card.Id}")).ProblemCodeAsync());
        await user.DeleteAsync($"/api/v1/finance/accounts/{empty.Id}").EnsureStatusAsync(HttpStatusCode.NoContent);
        Assert.Equal("category_has_children", await (await user.DeleteAsync($"/api/v1/finance/categories/{food.Id}")).ProblemCodeAsync());

        await user.DeleteAsync($"/api/v1/finance/categories/{restaurants.Id}").EnsureStatusAsync(HttpStatusCode.NoContent);
        Assert.Null((await user.GetJsonAsync<TransactionDto>($"/api/v1/finance/transactions/{tx.Id}")).CategoryId);

        var grandchild = await user.PostAsync("/api/v1/finance/categories", new CreateFinanceCategoryRequest("Too deep", CategoryKind.Expense, (await user.PostJsonAsync<CreateFinanceCategoryRequest, FinanceCategoryDto>("/api/v1/finance/categories", new("Sub", CategoryKind.Expense, food.Id))).Id));
        Assert.Equal("invalid_parent", await grandchild.ProblemCodeAsync());
    }

    [Fact]
    public async Task List_filters_by_period_account_category_kind_and_search()
    {
        var user = await env.CreateUserAsync();
        var card = await AccountAsync(user, "Card", "RUB", 0m);
        var cash = await AccountAsync(user, "Cash", "RUB", 0m);
        var food = await user.PostJsonAsync<CreateFinanceCategoryRequest, FinanceCategoryDto>("/api/v1/finance/categories", new("Food", CategoryKind.Expense, null));
        var bakery = await user.PostJsonAsync<CreateFinanceCategoryRequest, FinanceCategoryDto>("/api/v1/finance/categories", new("Bakery", CategoryKind.Expense, food.Id));
        await TransactionAsync(user, new(TransactionKind.Expense, Day, "Bread", card.Id, 50m, bakery.Id, null, null));
        await TransactionAsync(user, new(TransactionKind.Expense, Day.AddDays(20), "Cheese", cash.Id, 400m, food.Id, null, null));
        await TransactionAsync(user, new(TransactionKind.Income, Day, "Bonus", card.Id, 1000m, null, null, null));
        await TransactionAsync(user, new(TransactionKind.Transfer, Day, null, card.Id, 100m, null, cash.Id, null));

        Assert.Equal(2, (await ListAsync(user, $"categoryId={food.Id}")).TotalCount); // parent includes subcategory
        Assert.Equal(1, (await ListAsync(user, $"categoryId={bakery.Id}")).TotalCount);
        Assert.Equal(2, (await ListAsync(user, $"accountId={cash.Id}")).TotalCount); // includes incoming transfer
        Assert.Equal(1, (await ListAsync(user, "kind=Income")).TotalCount);
        Assert.Equal(3, (await ListAsync(user, $"from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}")).TotalCount);
        Assert.Equal("Cheese", Assert.Single((await ListAsync(user, "search=chee")).Items).Description);
        var sorted = await ListAsync(user, "sort=AmountDesc");
        Assert.Equal(1000m, sorted.Items[0].Amount);
    }

    [Fact]
    public async Task Budget_report_compares_plan_and_actual_per_currency_and_category()
    {
        var user = await env.CreateUserAsync();
        var card = await AccountAsync(user, "Card", "RUB", 0m);
        var eurCard = await AccountAsync(user, "EUR card", "EUR", 0m);
        var food = await user.PostJsonAsync<CreateFinanceCategoryRequest, FinanceCategoryDto>("/api/v1/finance/categories", new("Food", CategoryKind.Expense, null));
        var cafe = await user.PostJsonAsync<CreateFinanceCategoryRequest, FinanceCategoryDto>("/api/v1/finance/categories", new("Cafe", CategoryKind.Expense, food.Id));
        var foodBudget = await user.PostJsonAsync<CreateBudgetRequest, BudgetDto>("/api/v1/finance/budgets", new(food.Id, "RUB", 1000m, 80));
        var total = await user.PostJsonAsync<CreateBudgetRequest, BudgetDto>("/api/v1/finance/budgets", new(null, "RUB", 5000m, 90));
        await TransactionAsync(user, new(TransactionKind.Expense, Day, null, card.Id, 600m, food.Id, null, null));
        await TransactionAsync(user, new(TransactionKind.Expense, Day, null, card.Id, 250m, cafe.Id, null, null));
        await TransactionAsync(user, new(TransactionKind.Expense, Day, null, eurCard.Id, 99m, food.Id, null, null)); // other currency
        await TransactionAsync(user, new(TransactionKind.Expense, Day.AddMonths(1), null, card.Id, 999m, food.Id, null, null)); // other month

        var report = await user.GetJsonAsync<BudgetReportDto>($"/api/v1/finance/budgets/report?year={Day.Year}&month={Day.Month}");
        var foodUsage = report.Items.Single(i => i.BudgetId == foodBudget.Id);
        Assert.Equal(850m, foodUsage.Spent);
        Assert.Equal(150m, foodUsage.Remaining);
        Assert.Equal(85.0m, foodUsage.UsedPercent);
        Assert.Equal(BudgetState.Warning, foodUsage.State);
        Assert.Equal(850m, report.Items.Single(i => i.BudgetId == total.Id).Spent);

        var duplicate = await user.PostAsync("/api/v1/finance/budgets", new CreateBudgetRequest(food.Id, "RUB", 1m, 50));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var duplicateTotal = await user.PostAsync("/api/v1/finance/budgets", new CreateBudgetRequest(null, "RUB", 1m, 50));
        Assert.Equal(HttpStatusCode.Conflict, duplicateTotal.StatusCode);
    }

    [Fact]
    public async Task Recurring_transactions_post_due_occurrences_once_even_when_called_concurrently()
    {
        var user = await env.CreateUserAsync();
        var today = (await user.GetJsonAsync<ProfileDto>("/api/v1/profile")).Today;
        var card = await AccountAsync(user, "Card", "RUB", 0m);
        var start = today.AddMonths(-3);
        var template = await user.PostJsonAsync<SaveRecurringTransactionRequest, RecurringTransactionDto>("/api/v1/finance/recurring", new(
            TransactionKind.Expense, card.Id, 1200m, null, null, null, "Rent", new RecurrenceDto(RecurrenceFrequency.Monthly, 1, Weekdays.None, start, null), null));

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => user.PostAsync("/api/v1/finance/recurring/post-due", new { })));
        var posted = 0;
        foreach (var r in results)
        {
            posted += (await (await r.EnsureStatusAsync(HttpStatusCode.OK)).ReadJsonAsync<PostDueResult>()).Posted;
        }

        Assert.Equal(4, posted); // start, +1, +2, +3 months (today)
        Assert.Equal(-4800m, (await user.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{card.Id}")).Balance);
        Assert.Equal(0, (await user.PostJsonAsync<object, PostDueResult>("/api/v1/finance/recurring/post-due", new { }, HttpStatusCode.OK)).Posted);
        var reloaded = Assert.Single(await user.GetJsonAsync<List<RecurringTransactionDto>>("/api/v1/finance/recurring"));
        Assert.Equal(start.AddMonths(4), reloaded.NextOccurrence);
        Assert.Equal(template.Id, reloaded.Id);
    }

    [Fact]
    public async Task Reports_summarise_income_expense_and_categories_per_currency_excluding_transfers()
    {
        var user = await env.CreateUserAsync();
        var card = await AccountAsync(user, "Card", "RUB", 0m);
        var cash = await AccountAsync(user, "Cash", "RUB", 0m);
        var food = await user.PostJsonAsync<CreateFinanceCategoryRequest, FinanceCategoryDto>("/api/v1/finance/categories", new("Food", CategoryKind.Expense, null));
        var fun = await user.PostJsonAsync<CreateFinanceCategoryRequest, FinanceCategoryDto>("/api/v1/finance/categories", new("Fun", CategoryKind.Expense, null));
        await TransactionAsync(user, new(TransactionKind.Income, Day, null, card.Id, 1000m, null, null, null));
        await TransactionAsync(user, new(TransactionKind.Expense, Day, null, card.Id, 300m, food.Id, null, null));
        await TransactionAsync(user, new(TransactionKind.Expense, Day, null, card.Id, 100m, fun.Id, null, null));
        await TransactionAsync(user, new(TransactionKind.Transfer, Day, null, card.Id, 500m, null, cash.Id, null));

        var year = await user.GetJsonAsync<YearSummaryDto>($"/api/v1/finance/reports/year?year={Day.Year}");
        var rub = Assert.Single(year.Currencies);
        Assert.Equal(1000m, rub.TotalIncome);
        Assert.Equal(400m, rub.TotalExpense);
        Assert.Equal(600m, rub.Months[Day.Month - 1].Net);

        var breakdown = await user.GetJsonAsync<CategoryBreakdownDto>($"/api/v1/finance/reports/categories?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}&currencyCode=RUB");
        Assert.Equal(400m, breakdown.Total);
        Assert.Equal(75.0m, breakdown.Items.Single(i => i.CategoryId == food.Id).Percent);

        var period = await user.GetJsonAsync<PeriodSummaryDto>($"/api/v1/finance/reports/period?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}");
        Assert.Equal(600m, Assert.Single(period.Currencies).Net);
    }

    [Fact]
    public async Task Csv_export_is_bounded_and_neutralises_formulas()
    {
        var user = await env.CreateUserAsync();
        var card = await AccountAsync(user, "=HYPERLINK(\"http://evil\")", "RUB", 0m);
        await TransactionAsync(user, new(TransactionKind.Expense, Day, "@SUM(A1:A9) \"quoted\"", card.Id, 12.34m, null, null, null));

        var response = await user.GetAsync($"/api/v1/export/transactions.csv?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}").EnsureStatusAsync(HttpStatusCode.OK);
        var csv = await response.Content.ReadAsStringAsync();

        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("\"'=HYPERLINK(\"\"http://evil\"\")\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"'@SUM(A1:A9) \"\"quoted\"\"\"", csv, StringComparison.Ordinal);
        Assert.Contains("2026-09-15,expense,12.34,RUB", csv, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync("/api/v1/export/transactions.csv?from=2000-01-01&to=2026-01-01")).StatusCode);
    }

    private static async Task<AccountDto> AccountAsync(TestUser user, string name, string currency, decimal opening) =>
        await user.PostJsonAsync<CreateAccountRequest, AccountDto>("/api/v1/finance/accounts", new(name, AccountType.Checking, currency, opening, new DateOnly(2026, 1, 1)));

    private static Task<TransactionDto> TransactionAsync(TestUser user, CreateTransactionRequest request) =>
        user.PostJsonAsync<CreateTransactionRequest, TransactionDto>("/api/v1/finance/transactions", request);

    private static async Task<HttpResponseMessage> PostWithKeyAsync(TestUser user, CreateTransactionRequest request, string key)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/finance/transactions") { Content = JsonContent.Create(request, options: Json.Options) };
        message.Headers.Add(ApiHeaders.IdempotencyKey, key);
        return await user.Client.SendAsync(message);
    }

    private static async Task<decimal> TotalAsync(TestUser user, string currency) =>
        (await user.GetJsonAsync<AccountsOverviewDto>("/api/v1/finance/accounts")).TotalsByCurrency.Single(t => t.CurrencyCode == currency).Amount;

    private static Task<PagedResult<TransactionDto>> ListAsync(TestUser user, string query) =>
        user.GetJsonAsync<PagedResult<TransactionDto>>($"/api/v1/finance/transactions?{query}&pageSize=100");
}

internal static class OwnerContextExtensions
{
    /// <summary>The owner (migration) context has no current user, so query filters must be bypassed for assertions.</summary>
    public static IQueryable<T> IgnoreQueryFiltersForTests<T>(this Microsoft.EntityFrameworkCore.DbSet<T> set)
        where T : class => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(set);
}
