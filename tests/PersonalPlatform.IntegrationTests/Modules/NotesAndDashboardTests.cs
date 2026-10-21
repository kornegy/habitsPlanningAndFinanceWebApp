using System.Net;
using System.Text.Json;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Dashboard;
using PersonalPlatform.Contracts.Finance;
using PersonalPlatform.Contracts.Habits;
using PersonalPlatform.Contracts.Notes;
using PersonalPlatform.Contracts.Organization;
using PersonalPlatform.Contracts.Planner;
using PersonalPlatform.Contracts.Profile;
using PersonalPlatform.Contracts.Tasks;

namespace PersonalPlatform.IntegrationTests.Modules;

public sealed class NotesAndDashboardTests(ApiTestEnvironment env)
{
    [Fact]
    public async Task Full_text_search_matches_word_prefixes_in_any_language()
    {
        var user = await env.CreateUserAsync();
        await NoteAsync(user, "Список покупок", "Купить молоко и хлеб");
        await NoteAsync(user, "Meeting notes", "Discuss quarterly planning with the team");
        await NoteAsync(user, "Random", "nothing relevant");

        Assert.Equal("Список покупок", Assert.Single((await SearchAsync(user, "молок")).Items).Title);
        Assert.Equal("Meeting notes", Assert.Single((await SearchAsync(user, "QUARTER plan")).Items).Title);
        Assert.Empty((await SearchAsync(user, "молоко quarterly")).Items);
        Assert.Empty((await SearchAsync(user, "!!! :* & |")).Items); // tsquery syntax is never interpreted
    }

    [Fact]
    public async Task Pinned_first_tag_filter_archive_and_snippets()
    {
        var user = await env.CreateUserAsync();
        var tag = await user.PostJsonAsync<CreateTagRequest, TagDto>("/api/v1/tags", new("ideas"), HttpStatusCode.OK);
        var plain = await NoteAsync(user, "Plain", new string('a', 500));
        var pinned = await user.PostJsonAsync<CreateNoteRequest, NoteDto>("/api/v1/notes", new("Pinned", "x", [tag.Id], true));

        var list = await user.GetJsonAsync<PagedResult<NoteSummaryDto>>("/api/v1/notes");
        Assert.Equal(pinned.Id, list.Items[0].Id);
        Assert.Equal(160, list.Items.Single(n => n.Id == plain.Id).Snippet.Length);
        Assert.Equal(pinned.Id, Assert.Single((await user.GetJsonAsync<PagedResult<NoteSummaryDto>>($"/api/v1/notes?tagId={tag.Id}")).Items).Id);

        await user.PostJsonAsync<SetArchivedRequest, NoteDto>($"/api/v1/notes/{plain.Id}/archive", new(true), HttpStatusCode.OK);
        Assert.DoesNotContain((await user.GetJsonAsync<PagedResult<NoteSummaryDto>>("/api/v1/notes")).Items, n => n.Id == plain.Id);
        Assert.Contains((await user.GetJsonAsync<PagedResult<NoteSummaryDto>>("/api/v1/notes?archived=true")).Items, n => n.Id == plain.Id);
    }

    [Fact]
    public async Task Autosave_detects_concurrent_edits_and_keeps_bounded_history()
    {
        var user = await env.CreateUserAsync();
        var note = await NoteAsync(user, "Draft", "v0");

        var current = note;
        for (var i = 1; i <= 25; i++)
        {
            current = await user.PutJsonAsync<UpdateNoteRequest, NoteDto>($"/api/v1/notes/{note.Id}", new("Draft", $"v{i}", null, current.Version));
        }

        // A second tab still holding an old version must not overwrite the latest text.
        var conflict = await user.PutAsync($"/api/v1/notes/{note.Id}", new UpdateNoteRequest("Draft", "stale tab", null, note.Version));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("v25", (await user.GetJsonAsync<NoteDto>($"/api/v1/notes/{note.Id}")).Content);

        var revisions = await user.GetJsonAsync<List<NoteRevisionDto>>($"/api/v1/notes/{note.Id}/revisions");
        Assert.Equal(20, revisions.Count);
        Assert.Equal("v24", revisions[0].Content);
    }

    [Fact]
    public async Task Markup_in_notes_is_stored_and_returned_verbatim_as_plain_text()
    {
        var user = await env.CreateUserAsync();
        const string payload = "<script>alert(document.cookie)</script><img src=x onerror=alert(1)>";
        var note = await NoteAsync(user, "<b>title</b>", payload);

        var response = await user.GetAsync($"/api/v1/notes/{note.Id}");
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var reloaded = await response.ReadJsonAsync<NoteDto>();
        Assert.Equal(payload, reloaded.Content);
        Assert.Equal("<b>title</b>", reloaded.Title);
        // System.Text.Json escapes HTML-sensitive characters in the wire format.
        Assert.DoesNotContain("<script>", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Journal_has_one_entry_per_day_with_versioned_updates()
    {
        var user = await env.CreateUserAsync();
        var today = (await user.GetJsonAsync<ProfileDto>("/api/v1/profile")).Today;
        var url = $"/api/v1/journal/{today:yyyy-MM-dd}";

        var created = await user.PutJsonAsync<SaveJournalEntryRequest, JournalEntryDto>(url, new("Хороший день", 4, null));
        var withoutVersion = await user.PutAsync(url, new SaveJournalEntryRequest("overwrite?", 1, null));
        Assert.Equal(HttpStatusCode.Conflict, withoutVersion.StatusCode);

        var updated = await user.PutJsonAsync<SaveJournalEntryRequest, JournalEntryDto>(url, new("Хороший день, гуляли", 5, created.Version));
        Assert.Equal(5, updated.Mood);
        Assert.Equal(HttpStatusCode.Conflict, (await user.PutAsync(url, new SaveJournalEntryRequest("stale", 1, created.Version))).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await user.PutAsync($"/api/v1/journal/{today.AddDays(1):yyyy-MM-dd}", new SaveJournalEntryRequest("future", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PutAsync(url, new SaveJournalEntryRequest("x", 9, updated.Version))).StatusCode);

        Assert.Single((await user.GetJsonAsync<PagedResult<JournalEntryDto>>("/api/v1/journal?search=гуля")).Items);
        await user.DeleteAsync(url).EnsureStatusAsync(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Notes_export_contains_notes_and_journal()
    {
        var user = await env.CreateUserAsync();
        var today = (await user.GetJsonAsync<ProfileDto>("/api/v1/profile")).Today;
        await NoteAsync(user, "Exported", "content");
        await user.PutJsonAsync<SaveJournalEntryRequest, JournalEntryDto>($"/api/v1/journal/{today:yyyy-MM-dd}", new("diary", null, null));

        var response = await user.GetAsync("/api/v1/export/notes.json").EnsureStatusAsync(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Exported", json.RootElement.GetProperty("notes")[0].GetProperty("title").GetString());
        Assert.Equal("diary", json.RootElement.GetProperty("journal")[0].GetProperty("content").GetString());
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition!.DispositionType, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dashboard_summarises_the_day_from_real_data()
    {
        var user = await env.CreateUserAsync();
        var today = (await user.GetJsonAsync<ProfileDto>("/api/v1/profile")).Today;
        var habit = await user.PostJsonAsync<CreateHabitRequest, HabitDto>("/api/v1/habits", new("Walk", null, null, null, HabitScheduleType.Daily, Weekdays.None, 1, today, 7, null));
        await user.PutAsync($"/api/v1/habits/{habit.Id}/completions/{today:yyyy-MM-dd}", new { }).EnsureStatusAsync(HttpStatusCode.OK);
        await user.PostJsonAsync<CreateHabitRequest, HabitDto>("/api/v1/habits", new("Stretch", null, null, null, HabitScheduleType.Daily, Weekdays.None, 1, today, null, null));
        var todayTask = await user.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new("Today", null, TaskPriority.High, today, null, null, null, null, null, null));
        await user.PostAsync($"/api/v1/tasks/{todayTask.Id}/status", new SetTaskStatusRequest(TaskItemStatus.Done)).EnsureStatusAsync(HttpStatusCode.OK);
        await user.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new("Late", null, TaskPriority.Low, today.AddDays(-3), null, null, null, null, null, null));
        var account = await user.PostJsonAsync<CreateAccountRequest, AccountDto>("/api/v1/finance/accounts", new("Card", AccountType.Checking, "RUB", 0m, null));
        await user.PostJsonAsync<CreateTransactionRequest, TransactionDto>("/api/v1/finance/transactions", new(TransactionKind.Expense, today, null, account.Id, 700m, null, null, null));
        await user.PostJsonAsync<CreateBudgetRequest, BudgetDto>("/api/v1/finance/budgets", new(null, "RUB", 500m, 80));
        var soon = DateTimeOffset.UtcNow.AddMinutes(30);
        await user.PostJsonAsync<CreateTimeBlockRequest, TimeBlockSaveResult>("/api/v1/planner/blocks", new("Next up", null, soon, soon.AddMinutes(30), null, null));

        var dashboard = await user.GetJsonAsync<DashboardDto>("/api/v1/dashboard");

        Assert.Equal(today, dashboard.Today);
        Assert.Equal(2, dashboard.Habits.Count);
        Assert.Equal(1, dashboard.Progress.HabitsDone);
        Assert.Equal(1, dashboard.OverdueCount);
        Assert.Contains(dashboard.TasksToday, t => t.Title == "Late" && t.IsOverdue);
        Assert.Equal((1, 1), (dashboard.Progress.TasksDone, dashboard.Progress.TasksTotal));
        Assert.Equal(700m, Assert.Single(dashboard.MonthCashFlow).Expense);
        Assert.Equal(BudgetState.Exceeded, Assert.Single(dashboard.BudgetAlerts).State);
        Assert.Equal("Test User", dashboard.DisplayName);
    }

    [Fact]
    public async Task Theme_preference_is_saved_in_the_profile_and_validated()
    {
        var user = await env.CreateUserAsync();
        var profile = await user.GetJsonAsync<ProfileDto>("/api/v1/profile");
        Assert.Equal(ThemePreference.System, profile.Theme);

        var saved = await user.PutJsonAsync<UpdateProfileRequest, ProfileDto>("/api/v1/profile", new(
            profile.DisplayName, profile.TimeZoneId, profile.Locale, profile.CurrencyCode, profile.DateFormat, profile.FirstDayOfWeek,
            profile.AllowOverlappingTimeBlocks, ThemePreference.Dark, profile.Version));

        Assert.Equal(ThemePreference.Dark, saved.Theme);
        Assert.Equal(ThemePreference.Dark, (await user.GetJsonAsync<ProfileDto>("/api/v1/profile")).Theme);

        // An undefined value is rejected by validation rather than stored.
        var invalid = await user.PutAsync("/api/v1/profile", new
        {
            displayName = saved.DisplayName,
            timeZoneId = saved.TimeZoneId,
            locale = saved.Locale,
            currencyCode = saved.CurrencyCode,
            dateFormat = saved.DateFormat,
            firstDayOfWeek = saved.FirstDayOfWeek,
            allowOverlappingTimeBlocks = saved.AllowOverlappingTimeBlocks,
            theme = 7,
            version = saved.Version,
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Reminders_list_due_task_reminders_until_dismissed()
    {
        var user = await env.CreateUserAsync();
        var task = await user.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new("Call mom", null, TaskPriority.None, null, null, DateTimeOffset.UtcNow.AddMinutes(-1), null, null, null, null));
        await user.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new("Later", null, TaskPriority.None, null, null, DateTimeOffset.UtcNow.AddHours(5), null, null, null, null));

        var due = await user.GetJsonAsync<List<ReminderDto>>("/api/v1/reminders");
        Assert.Equal(task.Id, Assert.Single(due).Id);

        await user.PostAsync($"/api/v1/tasks/{task.Id}/dismiss-reminder", new { }).EnsureStatusAsync(HttpStatusCode.NoContent);
        Assert.Empty(await user.GetJsonAsync<List<ReminderDto>>("/api/v1/reminders"));
    }

    private static Task<NoteDto> NoteAsync(TestUser user, string title, string content) =>
        user.PostJsonAsync<CreateNoteRequest, NoteDto>("/api/v1/notes", new(title, content, null, false));

    private static Task<PagedResult<NoteSummaryDto>> SearchAsync(TestUser user, string search) =>
        user.GetJsonAsync<PagedResult<NoteSummaryDto>>($"/api/v1/notes?search={Uri.EscapeDataString(search)}");
}
