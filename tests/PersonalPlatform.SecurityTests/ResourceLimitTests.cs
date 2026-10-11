using System.Net;
using PersonalPlatform.Contracts.Finance;

namespace PersonalPlatform.SecurityTests;

/// <summary>Expensive-request protection and financial race conditions (scenarios 26-29).</summary>
public sealed class ResourceLimitTests(ApiTestEnvironment env)
{
    [Theory]
    [InlineData("/api/v1/tasks?pageSize=1000000000")]
    [InlineData("/api/v1/tasks?pageSize=101")]
    [InlineData("/api/v1/tasks?page=-1")]
    [InlineData("/api/v1/finance/transactions?pageSize=5000")]
    [InlineData("/api/v1/finance/transactions?from=1900-01-01&to=2099-01-01")]
    [InlineData("/api/v1/notes?pageSize=0")]
    [InlineData("/api/v1/journal?pageSize=999")]
    [InlineData("/api/v1/planner?from=2026-01-01&to=2026-03-01")]
    [InlineData("/api/v1/finance/reports/period?from=2020-01-01&to=2026-01-01")]
    [InlineData("/api/v1/finance/reports/categories?from=2020-01-01&to=2026-01-01&currencyCode=RUB")]
    [InlineData("/api/v1/finance/reports/year?year=1")]
    [InlineData("/api/v1/finance/budgets/report?year=2026&month=13")]
    [InlineData("/api/v1/export/transactions.csv?from=1990-01-01&to=2026-01-01")]
    [InlineData("/api/v1/tasks?search=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Unbounded_or_excessive_queries_are_rejected(string url)
    {
        var user = await env.CreateUserAsync();

        var response = await user.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_edits_of_one_transaction_cannot_both_win()
    {
        var user = await env.CreateUserAsync();
        var account = await user.PostJsonAsync<CreateAccountRequest, AccountDto>("/api/v1/finance/accounts", new("Card", AccountType.Checking, "RUB", 0m, null));
        var tx = await user.PostJsonAsync<CreateTransactionRequest, TransactionDto>("/api/v1/finance/transactions", new(TransactionKind.Expense, new DateOnly(2026, 9, 1), null, account.Id, 100m, null, null, null));

        var responses = await Task.WhenAll(new[] { 200m, 300m, 400m, 500m }.Select(amount =>
            user.PutAsync($"/api/v1/finance/transactions/{tx.Id}", new UpdateTransactionRequest(tx.OccurredOn, null, account.Id, amount, null, null, null, tx.Version))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var final = await user.GetJsonAsync<TransactionDto>($"/api/v1/finance/transactions/{tx.Id}");
        Assert.Equal(-final.Amount, (await user.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{account.Id}")).Balance);
    }

    [Fact]
    public async Task Concurrent_transfers_preserve_total_capital()
    {
        var user = await env.CreateUserAsync();
        var a = await user.PostJsonAsync<CreateAccountRequest, AccountDto>("/api/v1/finance/accounts", new("A", AccountType.Checking, "EUR", 1000m, null));
        var b = await user.PostJsonAsync<CreateAccountRequest, AccountDto>("/api/v1/finance/accounts", new("B", AccountType.Savings, "EUR", 1000m, null));

        var transfers = Enumerable.Range(0, 30).Select(i => i % 2 == 0
            ? new CreateTransactionRequest(TransactionKind.Transfer, new DateOnly(2026, 9, 1), null, a.Id, 7.77m, null, b.Id, null)
            : new CreateTransactionRequest(TransactionKind.Transfer, new DateOnly(2026, 9, 1), null, b.Id, 3.33m, null, a.Id, null));
        await Task.WhenAll(transfers.Select(t => user.PostAsync("/api/v1/finance/transactions", t)));

        var overview = await user.GetJsonAsync<AccountsOverviewDto>("/api/v1/finance/accounts");
        Assert.Equal(2000m, overview.TotalsByCurrency.Single(t => t.CurrencyCode == "EUR").Amount);
        Assert.Equal(1000m - (15 * 7.77m) + (15 * 3.33m), overview.Accounts.Single(x => x.Id == a.Id).Balance);
    }
}
