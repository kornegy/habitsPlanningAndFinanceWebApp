using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PersonalPlatform.Contracts.Finance;
using PersonalPlatform.Contracts.Habits;
using PersonalPlatform.Contracts.Notes;
using PersonalPlatform.Contracts.Organization;
using PersonalPlatform.Contracts.Planner;
using PersonalPlatform.Contracts.Profile;
using PersonalPlatform.Contracts.Tasks;
using PersonalPlatform.Infrastructure.Persistence;

namespace PersonalPlatform.SecurityTests;

/// <summary>
/// Broken object level authorisation (BOLA/IDOR) and cross-user references, for every module.
/// Scenarios 1-11, 25 of the security test plan. Foreign objects must behave exactly like missing ones (404).
/// </summary>
public sealed class DataIsolationTests(ApiTestEnvironment env)
{
    [Fact]
    public async Task Another_user_cannot_read_any_object_by_id()
    {
        var (victim, data) = await VictimAsync();
        var attacker = await env.CreateUserAsync();

        foreach (var url in data.ReadUrls)
        {
            var response = await attacker.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"GET {url} returned {(int)response.StatusCode}");
            Assert.Equal(await NotFoundBodyAsync(attacker), Normalise(await response.Content.ReadAsStringAsync()));
        }

        // The owner can read all of them (guards against a test that passes because the URLs are wrong).
        foreach (var url in data.ReadUrls)
        {
            await victim.GetAsync(url).EnsureStatusAsync(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Another_user_cannot_modify_or_delete_any_object()
    {
        var (victim, d) = await VictimAsync();
        var attacker = await env.CreateUserAsync();
        var start = new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);

        var attempts = new List<(string Name, Func<Task<HttpResponseMessage>> Call)>
        {
            ("update task", () => attacker.PutAsync($"/api/v1/tasks/{d.TaskId}", new UpdateTaskRequest("pwned", null, TaskPriority.High, null, null, null, null, null, null, 0))),
            ("complete task", () => attacker.PostAsync($"/api/v1/tasks/{d.TaskId}/status", new SetTaskStatusRequest(TaskItemStatus.Done))),
            ("reschedule task", () => attacker.PostAsync($"/api/v1/tasks/{d.TaskId}/reschedule", new RescheduleTaskRequest(null, null))),
            ("subtask", () => attacker.PostAsync($"/api/v1/tasks/{d.TaskId}/subtasks/{d.SubtaskId}/done", new SetSubtaskDoneRequest(true))),
            ("dismiss reminder", () => attacker.PostAsync($"/api/v1/tasks/{d.TaskId}/dismiss-reminder", new { })),
            ("update series", () => attacker.PutAsync($"/api/v1/task-series/{d.SeriesId}", new UpdateTaskSeriesRequest("pwned", null, TaskPriority.None, null, null, new RecurrenceDto(RecurrenceFrequency.Daily, 1, Weekdays.None, new DateOnly(2026, 1, 1), null), 0))),
            ("stop series", () => attacker.PostAsync($"/api/v1/task-series/{d.SeriesId}/stop", new { })),
            ("update category", () => attacker.PutAsync($"/api/v1/categories/{d.CategoryId}", new UpdateCategoryRequest("pwned", null, 0))),
            ("update habit", () => attacker.PutAsync($"/api/v1/habits/{d.HabitId}", new UpdateHabitRequest("pwned", null, null, null, HabitScheduleType.Daily, Weekdays.None, 1, new DateOnly(2026, 1, 1), null, null, 0))),
            ("archive habit", () => attacker.PostAsync($"/api/v1/habits/{d.HabitId}/archive", new SetArchivedRequest(true))),
            ("check off habit", () => attacker.PutAsync($"/api/v1/habits/{d.HabitId}/completions/{d.Today:yyyy-MM-dd}", new { })),
            ("uncheck habit", () => attacker.DeleteAsync($"/api/v1/habits/{d.HabitId}/completions/{d.Today:yyyy-MM-dd}")),
            ("update block", () => attacker.PutAsync($"/api/v1/planner/blocks/{d.BlockId}", new UpdateTimeBlockRequest("pwned", null, start, start.AddHours(1), null, 0))),
            ("move block", () => attacker.PostAsync($"/api/v1/planner/blocks/{d.BlockId}/move", new MoveTimeBlockRequest(start, start.AddHours(1), 0))),
            ("update account", () => attacker.PutAsync($"/api/v1/finance/accounts/{d.AccountId}", new UpdateAccountRequest("pwned", AccountType.Cash, 1_000_000m, new DateOnly(2026, 1, 1), 0))),
            ("archive account", () => attacker.PostAsync($"/api/v1/finance/accounts/{d.AccountId}/archive", new SetArchivedRequest(true))),
            ("update finance category", () => attacker.PutAsync($"/api/v1/finance/categories/{d.FinanceCategoryId}", new UpdateFinanceCategoryRequest("pwned", null, false, 0))),
            ("update transaction", () => attacker.PutAsync($"/api/v1/finance/transactions/{d.TransactionId}", new UpdateTransactionRequest(new DateOnly(2026, 1, 1), null, d.AccountId, 1m, null, null, null, 0))),
            ("update budget", () => attacker.PutAsync($"/api/v1/finance/budgets/{d.BudgetId}", new UpdateBudgetRequest(1m, 50, 0))),
            ("update recurring", () => attacker.PutAsync($"/api/v1/finance/recurring/{d.RecurringId}", new SaveRecurringTransactionRequest(TransactionKind.Expense, d.AccountId, 1m, null, null, null, null, new RecurrenceDto(RecurrenceFrequency.Monthly, 1, Weekdays.None, new DateOnly(2026, 1, 1), null), 0))),
            ("pause recurring", () => attacker.PostAsync($"/api/v1/finance/recurring/{d.RecurringId}/pause", new SetPausedRequest(true))),
            ("update note", () => attacker.PutAsync($"/api/v1/notes/{d.NoteId}", new UpdateNoteRequest("pwned", "pwned", null, 0))),
            ("pin note", () => attacker.PostAsync($"/api/v1/notes/{d.NoteId}/pin", new SetPinnedRequest(true))),
            ("archive note", () => attacker.PostAsync($"/api/v1/notes/{d.NoteId}/archive", new SetArchivedRequest(true))),
            ("delete session", () => attacker.DeleteAsync($"/api/v1/account/sessions/{d.SessionId}")),
        };
        attempts.AddRange(d.DeleteUrls.Select(url => ($"delete {url}", (Func<Task<HttpResponseMessage>>)(() => attacker.DeleteAsync(url)))));

        foreach (var (name, call) in attempts)
        {
            var response = await call();
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{name} returned {(int)response.StatusCode}");
        }

        // Nothing changed for the victim.
        foreach (var url in d.ReadUrls)
        {
            await victim.GetAsync(url).EnsureStatusAsync(HttpStatusCode.OK);
        }

        Assert.Equal("Victim task", (await victim.GetJsonAsync<TaskDto>($"/api/v1/tasks/{d.TaskId}")).Title);
        Assert.Equal(TaskItemStatus.Open, (await victim.GetJsonAsync<TaskDto>($"/api/v1/tasks/{d.TaskId}")).Status);
        Assert.Equal(400m, (await victim.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{d.AccountId}")).Balance);
        Assert.False((await victim.GetJsonAsync<HabitDto>($"/api/v1/habits/{d.HabitId}")).IsArchived);
        await victim.GetAsync("/api/v1/profile").EnsureStatusAsync(HttpStatusCode.OK); // victim's session alive
    }

    [Fact]
    public async Task Lists_searches_reports_and_exports_never_include_other_users_data()
    {
        var (_, d) = await VictimAsync();
        var attacker = await env.CreateUserAsync();
        var marker = d.Marker;

        var responses = new[]
        {
            "/api/v1/tasks?pageSize=100", $"/api/v1/tasks?search={marker}", "/api/v1/habits?includeArchived=true", "/api/v1/categories", "/api/v1/tags",
            "/api/v1/planner?from=2026-11-01&to=2026-12-12", "/api/v1/finance/accounts?includeArchived=true", "/api/v1/finance/categories",
            "/api/v1/finance/transactions?pageSize=100", $"/api/v1/finance/transactions?search={marker}", "/api/v1/finance/budgets",
            "/api/v1/finance/recurring", "/api/v1/notes?pageSize=100", $"/api/v1/notes?search={marker}", "/api/v1/notes?archived=true",
            $"/api/v1/journal?search={marker}", "/api/v1/dashboard", "/api/v1/reminders", "/api/v1/account/sessions",
            "/api/v1/finance/reports/year?year=2026", "/api/v1/finance/reports/period?from=2026-01-01&to=2026-12-31",
            "/api/v1/finance/reports/categories?from=2026-01-01&to=2026-12-31&currencyCode=RUB", "/api/v1/finance/budgets/report?year=2026&month=9",
            "/api/v1/export/transactions.csv?from=2020-01-01&to=2026-12-31", "/api/v1/export/notes.json",
        };

        foreach (var url in responses)
        {
            var body = await (await attacker.GetAsync(url).EnsureStatusAsync(HttpStatusCode.OK)).Content.ReadAsStringAsync();
            Assert.DoesNotContain(marker, body, StringComparison.Ordinal);
            foreach (var id in d.AllIds)
            {
                Assert.DoesNotContain(id.ToString(), body, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Another_users_objects_cannot_be_referenced_from_own_objects()
    {
        var (victim, d) = await VictimAsync();
        var attacker = await env.CreateUserAsync();
        var myAccount = await attacker.PostJsonAsync<CreateAccountRequest, AccountDto>("/api/v1/finance/accounts", new("Mine", AccountType.Cash, "RUB", 0m, null));
        var start = new DateTimeOffset(2026, 12, 2, 10, 0, 0, TimeSpan.Zero);

        var attempts = new (string Name, Func<Task<HttpResponseMessage>> Call)[]
        {
            ("task with foreign category", () => attacker.PostAsync("/api/v1/tasks", new CreateTaskRequest("x", null, TaskPriority.None, null, null, null, d.CategoryId, null, null, null))),
            ("task with foreign tag", () => attacker.PostAsync("/api/v1/tasks", new CreateTaskRequest("x", null, TaskPriority.None, null, null, null, null, [d.TagId], null, null))),
            ("habit with foreign category", () => attacker.PostAsync("/api/v1/habits", new CreateHabitRequest("x", null, null, d.CategoryId, HabitScheduleType.Daily, Weekdays.None, 1, null, null, null))),
            ("block for foreign task", () => attacker.PostAsync("/api/v1/planner/blocks", new CreateTimeBlockRequest("x", null, start, start.AddHours(1), d.TaskId, null))),
            ("expense from foreign account", () => attacker.PostAsync("/api/v1/finance/transactions", new CreateTransactionRequest(TransactionKind.Expense, d.Today, null, d.AccountId, 1m, null, null, null))),
            ("transfer into foreign account", () => attacker.PostAsync("/api/v1/finance/transactions", new CreateTransactionRequest(TransactionKind.Transfer, d.Today, null, myAccount.Id, 1m, null, d.AccountId, null))),
            ("expense with foreign category", () => attacker.PostAsync("/api/v1/finance/transactions", new CreateTransactionRequest(TransactionKind.Expense, d.Today, null, myAccount.Id, 1m, d.FinanceCategoryId, null, null))),
            ("subcategory of foreign category", () => attacker.PostAsync("/api/v1/finance/categories", new CreateFinanceCategoryRequest("x", CategoryKind.Expense, d.FinanceCategoryId))),
            ("budget on foreign category", () => attacker.PostAsync("/api/v1/finance/budgets", new CreateBudgetRequest(d.FinanceCategoryId, "RUB", 1m, 50))),
            ("recurring on foreign account", () => attacker.PostAsync("/api/v1/finance/recurring", new SaveRecurringTransactionRequest(TransactionKind.Expense, d.AccountId, 1m, null, null, null, null, new RecurrenceDto(RecurrenceFrequency.Monthly, 1, Weekdays.None, d.Today, null), null))),
            ("note with foreign tag", () => attacker.PostAsync("/api/v1/notes", new CreateNoteRequest("x", "x", [d.TagId], false))),
        };

        foreach (var (name, call) in attempts)
        {
            var response = await call();
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{name} returned {(int)response.StatusCode}");
        }

        // Moving one's own transaction onto a foreign account is equally impossible.
        var mine = await attacker.PostJsonAsync<CreateTransactionRequest, TransactionDto>("/api/v1/finance/transactions", new(TransactionKind.Expense, d.Today, null, myAccount.Id, 5m, null, null, null));
        var hijack = await attacker.PutAsync($"/api/v1/finance/transactions/{mine.Id}", new UpdateTransactionRequest(d.Today, null, d.AccountId, 5m, null, null, null, mine.Version));
        Assert.Equal(HttpStatusCode.NotFound, hijack.StatusCode);
        Assert.Equal(400m, (await victim.GetJsonAsync<AccountDto>($"/api/v1/finance/accounts/{d.AccountId}")).Balance);

        var bulk = await attacker.PostJsonAsync<BulkTaskRequest, BulkTaskResult>("/api/v1/tasks/bulk", new([d.TaskId], BulkTaskAction.Delete, null), HttpStatusCode.OK);
        Assert.Equal(0, bulk.Affected);
        await victim.GetAsync($"/api/v1/tasks/{d.TaskId}").EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/v1/tasks", """{"title":"x","priority":"None","ownerId":"{victim}"}""")]
    [InlineData("/api/v1/finance/accounts", """{"name":"x","type":"Cash","currencyCode":"RUB","openingBalance":0,"balance":1000000}""")]
    [InlineData("/api/v1/finance/transactions", """{"kind":"Expense","occurredOn":"2026-09-01","accountId":"{victim}","amount":1,"userId":"{victim}"}""")]
    [InlineData("/api/v1/notes", """{"title":"x","content":"y","isPinned":false,"id":"{victim}"}""")]
    [InlineData("/api/v1/habits", """{"name":"x","scheduleType":"Daily","days":"None","intervalDays":1,"currentStreak":999}""")]
    public async Task Unknown_or_protected_fields_in_request_bodies_are_rejected(string url, string json)
    {
        var victim = await env.CreateUserAsync();
        var attacker = await env.CreateUserAsync();
        using var content = new StringContent(json.Replace("{victim}", victim.Id.ToString(), StringComparison.Ordinal), Encoding.UTF8, "application/json");

        var response = await attacker.Client.PostAsync(url, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Owner_is_always_taken_from_the_token_not_from_the_request()
    {
        var victim = await env.CreateUserAsync();
        var attacker = await env.CreateUserAsync();

        // Even query-string and header "identity" values are ignored.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tasks?ownerId={victim.Id}&userId={victim.Id}")
        {
            Content = JsonContent.Create(new CreateTaskRequest("attacker's task", null, TaskPriority.None, null, null, null, null, null, null, null), options: Json.Options),
        };
        request.Headers.Add("X-User-Id", victim.Id.ToString());
        var created = await (await attacker.Client.SendAsync(request).EnsureStatusAsync(HttpStatusCode.Created)).ReadJsonAsync<TaskDto>();

        await attacker.GetAsync($"/api/v1/tasks/{created.Id}").EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.NotFound, (await victim.GetAsync($"/api/v1/tasks/{created.Id}")).StatusCode);
        await using var db = env.Database.CreateOwnerContext();
        var owner = await db.Tasks.IgnoreQueryFilters().Where(t => t.Id == created.Id).Select(t => t.OwnerId).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(attacker.Id, owner);
    }

    [Fact]
    public async Task Database_rejects_cross_user_references_even_if_application_checks_were_bypassed()
    {
        var (_, d) = await VictimAsync();
        var attacker = await env.CreateUserAsync();

        // Simulate an application bug: write directly as the application role, referencing the victim's account.
        await using var connection = new NpgsqlConnection(env.Database.AppConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO app.transactions (id, owner_id, kind, occurred_on, account_id, amount, currency_code, created_at, updated_at)
            VALUES (@id, @attacker, 0, DATE '2026-09-01', @account, 1, 'RUB', now(), now())
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("attacker", attacker.Id);
        command.Parameters.AddWithValue("account", d.AccountId);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task Persistence_layer_filters_and_refuses_foreign_rows_without_explicit_owner_conditions()
    {
        var (victim, d) = await VictimAsync();
        var attacker = await env.CreateUserAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(env.Database.AppConnectionString).UseSnakeCaseNamingConvention().Options;

        await using var asAttacker = new AppDbContext(options, new PostgresFixture.FixedUser(attacker.Id));
        Assert.Empty(await asAttacker.Transactions.Where(t => t.Id == d.TransactionId).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await asAttacker.Notes.CountAsync(TestContext.Current.CancellationToken));

        var foreignNote = await asAttacker.Notes.IgnoreQueryFilters().SingleAsync(n => n.Id == d.NoteId, TestContext.Current.CancellationToken);
        foreignNote.Edit("pwned", "pwned", DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => asAttacker.SaveChangesAsync(TestContext.Current.CancellationToken));

        await using var anonymous = new AppDbContext(options, new PostgresFixture.FixedUser(null));
        Assert.Equal(0, await anonymous.Tasks.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Victim note", (await victim.GetJsonAsync<NoteDto>($"/api/v1/notes/{d.NoteId}")).Title);
    }

    private async Task<string> NotFoundBodyAsync(TestUser user) =>
        Normalise(await (await user.GetAsync($"/api/v1/tasks/{Guid.NewGuid()}")).Content.ReadAsStringAsync());

    private static string Normalise(string problemJson) =>
        System.Text.RegularExpressions.Regex.Replace(problemJson, "\"traceId\":\"[0-9a-f]+\"", "\"traceId\":\"\"");

    [Fact]
    public async Task Habits_to_quit_and_habit_resets_are_scoped_to_their_owner()
    {
        var (victim, d) = await VictimAsync();
        var quit = (await victim.PostJsonAsync<CreateQuitHabitRequest, QuitHabitDetailDto>("/api/v1/quit-habits", new($"Quit {d.Marker}", $"why {d.Marker}", null, d.Today.AddDays(-3)))).Habit;
        await victim.PostJsonAsync<LogRelapseRequest, QuitHabitDetailDto>($"/api/v1/quit-habits/{quit.Id}/relapses", new(d.Today.AddDays(-1), $"note {d.Marker}"), HttpStatusCode.OK);
        var attacker = await env.CreateUserAsync();
        await attacker.PostAsync("/api/v1/habits/reset", new { }).EnsureStatusAsync(HttpStatusCode.OK); // must not touch the victim's habits

        var attempts = new List<(string Name, Func<Task<HttpResponseMessage>> Call)>
        {
            ("get quit habit", () => attacker.GetAsync($"/api/v1/quit-habits/{quit.Id}")),
            ("update quit habit", () => attacker.PutAsync($"/api/v1/quit-habits/{quit.Id}", new UpdateQuitHabitRequest("pwned", null, null, d.Today, 0))),
            ("archive quit habit", () => attacker.PostAsync($"/api/v1/quit-habits/{quit.Id}/archive", new SetArchivedRequest(true))),
            ("log relapse", () => attacker.PostAsync($"/api/v1/quit-habits/{quit.Id}/relapses", new LogRelapseRequest(d.Today, null))),
            ("delete relapse", () => attacker.DeleteAsync($"/api/v1/quit-habits/{quit.Id}/relapses/{d.Today.AddDays(-1):yyyy-MM-dd}")),
            ("delete quit habit", () => attacker.DeleteAsync($"/api/v1/quit-habits/{quit.Id}")),
            ("reset habit", () => attacker.PostAsync($"/api/v1/habits/{d.HabitId}/reset", new { })),
        };
        foreach (var (name, call) in attempts)
        {
            var response = await call();
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{name} returned {(int)response.StatusCode}");
        }

        var list = await (await attacker.GetAsync("/api/v1/quit-habits?includeArchived=true").EnsureStatusAsync(HttpStatusCode.OK)).Content.ReadAsStringAsync();
        Assert.DoesNotContain(d.Marker, list, StringComparison.Ordinal);
        Assert.DoesNotContain(quit.Id.ToString(), list, StringComparison.OrdinalIgnoreCase);

        var after = await victim.GetJsonAsync<QuitHabitDetailDto>($"/api/v1/quit-habits/{quit.Id}");
        Assert.Equal((quit.Name, false, 1), (after.Habit.Name, after.Habit.IsArchived, after.Habit.RelapseCount));
        Assert.True((await victim.GetJsonAsync<HabitDto>($"/api/v1/habits/{d.HabitId}")).CompletedToday);
    }

    private async Task<(TestUser Victim, VictimData Data)> VictimAsync()
    {
        var victim = await env.CreateUserAsync();
        var today = (await victim.GetJsonAsync<ProfileDto>("/api/v1/profile")).Today;
        var marker = "zq" + Guid.NewGuid().ToString("N")[..10];
        var category = await victim.PostJsonAsync<CreateCategoryRequest, CategoryDto>("/api/v1/categories", new(CategoryScope.Task, $"Cat {marker}", null));
        var tag = await victim.PostJsonAsync<CreateTagRequest, TagDto>("/api/v1/tags", new($"tag{marker}"), HttpStatusCode.OK);
        var task = await victim.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new("Victim task", $"secret {marker}", TaskPriority.High, today, null, DateTimeOffset.UtcNow.AddMinutes(-1), category.Id, [tag.Id], [new("sub", false)], null));
        var recurringTask = await victim.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new($"Recurring {marker}", null, TaskPriority.None, null, null, null, null, null, null, new RecurrenceDto(RecurrenceFrequency.Weekly, 1, Weekdays.None, today, null)));
        var habit = await victim.PostJsonAsync<CreateHabitRequest, HabitDto>("/api/v1/habits", new($"Habit {marker}", null, null, null, HabitScheduleType.Daily, Weekdays.None, 1, today, null, null));
        await victim.PutAsync($"/api/v1/habits/{habit.Id}/completions/{today:yyyy-MM-dd}", new { }).EnsureStatusAsync(HttpStatusCode.OK);
        var start = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
        var block = await victim.PostJsonAsync<CreateTimeBlockRequest, TimeBlockSaveResult>("/api/v1/planner/blocks", new($"Block {marker}", null, start, start.AddHours(1), task.Id, null));
        var account = await victim.PostJsonAsync<CreateAccountRequest, AccountDto>("/api/v1/finance/accounts", new($"Account {marker}", AccountType.Checking, "RUB", 1000m, null));
        var financeCategory = await victim.PostJsonAsync<CreateFinanceCategoryRequest, FinanceCategoryDto>("/api/v1/finance/categories", new($"Fin {marker}", CategoryKind.Expense, null));
        var transaction = await victim.PostJsonAsync<CreateTransactionRequest, TransactionDto>("/api/v1/finance/transactions", new(TransactionKind.Expense, new DateOnly(2026, 9, 10), $"Purchase {marker}", account.Id, 600m, financeCategory.Id, null, null));
        var budget = await victim.PostJsonAsync<CreateBudgetRequest, BudgetDto>("/api/v1/finance/budgets", new(financeCategory.Id, "RUB", 900m, 80));
        var recurring = await victim.PostJsonAsync<SaveRecurringTransactionRequest, RecurringTransactionDto>("/api/v1/finance/recurring", new(TransactionKind.Expense, account.Id, 10m, null, null, null, $"Rec {marker}", new RecurrenceDto(RecurrenceFrequency.Monthly, 1, Weekdays.None, today.AddDays(1), null), null));
        var note = await victim.PostJsonAsync<CreateNoteRequest, NoteDto>("/api/v1/notes", new("Victim note", $"private {marker}", [tag.Id], false));
        await victim.PutJsonAsync<SaveJournalEntryRequest, JournalEntryDto>($"/api/v1/journal/{today:yyyy-MM-dd}", new($"Dear diary {marker}", 3, null));
        var sessionId = TokenForge.SessionIdOf(victim.AccessToken);

        var data = new VictimData(
            today, marker, category.Id, tag.Id, task.Id, task.Subtasks[0].Id, recurringTask.SeriesId!.Value, habit.Id, block.Block.Id,
            account.Id, financeCategory.Id, transaction.Id, budget.Id, recurring.Id, note.Id, sessionId);
        return (victim, data);
    }

    private sealed record VictimData(
        DateOnly Today,
        string Marker,
        Guid CategoryId,
        Guid TagId,
        Guid TaskId,
        Guid SubtaskId,
        Guid SeriesId,
        Guid HabitId,
        Guid BlockId,
        Guid AccountId,
        Guid FinanceCategoryId,
        Guid TransactionId,
        Guid BudgetId,
        Guid RecurringId,
        Guid NoteId,
        Guid SessionId)
    {
        public IEnumerable<Guid> AllIds => [CategoryId, TagId, TaskId, SubtaskId, SeriesId, HabitId, BlockId, AccountId, FinanceCategoryId, TransactionId, BudgetId, RecurringId, NoteId, SessionId];

        public IEnumerable<string> ReadUrls =>
        [
            $"/api/v1/tasks/{TaskId}", $"/api/v1/task-series/{SeriesId}", $"/api/v1/habits/{HabitId}",
            $"/api/v1/habits/{HabitId}/history?from={Today.AddDays(-7):yyyy-MM-dd}&to={Today:yyyy-MM-dd}",
            $"/api/v1/habits/{HabitId}/stats?from={Today.AddDays(-7):yyyy-MM-dd}&to={Today:yyyy-MM-dd}",
            $"/api/v1/planner/blocks/{BlockId}", $"/api/v1/finance/accounts/{AccountId}", $"/api/v1/finance/transactions/{TransactionId}",
            $"/api/v1/notes/{NoteId}", $"/api/v1/notes/{NoteId}/revisions", $"/api/v1/journal/{Today:yyyy-MM-dd}",
        ];

        public IEnumerable<string> DeleteUrls =>
        [
            $"/api/v1/tasks/{TaskId}", $"/api/v1/task-series/{SeriesId}", $"/api/v1/categories/{CategoryId}", $"/api/v1/tags/{TagId}",
            $"/api/v1/habits/{HabitId}", $"/api/v1/planner/blocks/{BlockId}", $"/api/v1/finance/accounts/{AccountId}",
            $"/api/v1/finance/categories/{FinanceCategoryId}", $"/api/v1/finance/transactions/{TransactionId}", $"/api/v1/finance/budgets/{BudgetId}",
            $"/api/v1/finance/recurring/{RecurringId}", $"/api/v1/notes/{NoteId}", $"/api/v1/journal/{Today:yyyy-MM-dd}",
        ];
    }
}
