using System.Net;
using System.Text.Json;
using PersonalPlatform.Contracts.Habits;
using PersonalPlatform.Contracts.Planner;
using PersonalPlatform.Contracts.Profile;
using PersonalPlatform.Contracts.Tasks;

namespace PersonalPlatform.IntegrationTests.Modules;

public sealed class PlannerTests(ApiTestEnvironment env)
{
    [Fact]
    public async Task Overlaps_are_reported_when_allowed()
    {
        var user = await env.CreateUserAsync();
        var start = new DateTimeOffset(2026, 11, 2, 7, 0, 0, TimeSpan.Zero);

        var first = await CreateAsync(user, "Deep work", start, start.AddHours(2));
        var second = await CreateAsync(user, "Call", start.AddHours(1), start.AddHours(1.5));

        Assert.Empty(first.Conflicts);
        Assert.Equal(first.Block.Id, Assert.Single(second.Conflicts).Id);

        var range = await user.GetJsonAsync<PlannerRangeDto>("/api/v1/planner?from=2026-11-02&to=2026-11-02");
        Assert.Equal(2, range.Blocks.Count);
        Assert.Single(range.Conflicts);
    }

    [Fact]
    public async Task Overlaps_are_rejected_when_disabled_in_settings_even_under_concurrency()
    {
        var user = await env.CreateUserAsync();
        var profile = await user.GetJsonAsync<ProfileDto>("/api/v1/profile");
        await user.PutJsonAsync<UpdateProfileRequest, ProfileDto>("/api/v1/profile", new(
            profile.DisplayName, profile.TimeZoneId, profile.Locale, profile.CurrencyCode, profile.DateFormat, profile.FirstDayOfWeek, false, profile.Version));
        var start = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            user.PostAsync("/api/v1/planner/blocks", new CreateTimeBlockRequest($"Block {i}", null, start.AddMinutes(i * 10), start.AddMinutes(60 + (i * 10)), null, null))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        var rejected = responses.First(r => r.StatusCode != HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("time_block_overlap", await rejected.ProblemCodeAsync());
        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("details").GetArrayLength());

        // Adjacent (touching) blocks do not overlap: intervals are half-open.
        var adjacent = await user.PostAsync("/api/v1/planner/blocks", new CreateTimeBlockRequest("Next", null, start.AddHours(5), start.AddHours(6), null, null));
        var adjacent2 = await user.PostAsync("/api/v1/planner/blocks", new CreateTimeBlockRequest("After", null, start.AddHours(6), start.AddHours(7), null, null));
        Assert.Equal(HttpStatusCode.Created, adjacent.StatusCode);
        Assert.Equal(HttpStatusCode.Created, adjacent2.StatusCode);
    }

    [Fact]
    public async Task Quick_task_from_calendar_is_created_atomically_and_due_on_the_local_date()
    {
        var user = await env.CreateUserAsync(); // Europe/Moscow (UTC+3)
        var startsAt = new DateTimeOffset(2026, 11, 4, 22, 30, 0, TimeSpan.Zero); // 01:30 on Nov 5 in Moscow

        var result = await user.PostJsonAsync<CreateTimeBlockRequest, TimeBlockSaveResult>("/api/v1/planner/blocks",
            new("Plan trip", null, startsAt, startsAt.AddMinutes(45), null, "Book tickets"));

        Assert.NotNull(result.Block.TaskId);
        Assert.Equal("Book tickets", result.Block.TaskTitle);
        var task = await user.GetJsonAsync<TaskDto>($"/api/v1/tasks/{result.Block.TaskId}");
        Assert.Equal(new DateOnly(2026, 11, 5), task.DueDate);
    }

    [Fact]
    public async Task Move_resize_validation_and_version_checks()
    {
        var user = await env.CreateUserAsync();
        var start = new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);
        var created = await CreateAsync(user, "Read", start, start.AddHours(1));

        var moved = await user.PostJsonAsync<MoveTimeBlockRequest, TimeBlockSaveResult>($"/api/v1/planner/blocks/{created.Block.Id}/move",
            new(start.AddHours(3), start.AddHours(5), created.Block.Version), HttpStatusCode.OK);
        Assert.Equal(TimeSpan.FromHours(2), moved.Block.EndsAt - moved.Block.StartsAt);

        var stale = await user.PostAsync($"/api/v1/planner/blocks/{created.Block.Id}/move", new MoveTimeBlockRequest(start, start.AddHours(1), created.Block.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var endBeforeStart = await user.PostAsync("/api/v1/planner/blocks", new CreateTimeBlockRequest("x", null, start, start.AddMinutes(-5), null, null));
        var tooShort = await user.PostAsync("/api/v1/planner/blocks", new CreateTimeBlockRequest("x", null, start, start.AddMinutes(2), null, null));
        var tooLong = await user.PostAsync("/api/v1/planner/blocks", new CreateTimeBlockRequest("x", null, start, start.AddHours(25), null, null));
        Assert.Equal(HttpStatusCode.BadRequest, endBeforeStart.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync("/api/v1/planner?from=2026-01-01&to=2026-12-31")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync("/api/v1/planner?from=2026-02-01&to=2026-01-01")).StatusCode);
    }

    [Fact]
    public async Task Range_includes_due_habits_and_tasks_and_respects_the_user_time_zone()
    {
        var user = await env.CreateUserAsync(); // Europe/Moscow
        await user.PostJsonAsync<CreateHabitRequest, HabitDto>("/api/v1/habits", new("Stretch", null, null, null, HabitScheduleType.Daily, Weekdays.None, 1, new DateOnly(2026, 11, 1), null, null));
        await user.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new("Pay rent", null, TaskPriority.High, new DateOnly(2026, 11, 10), null, null, null, null, null, null));
        // 23:30 UTC on Nov 9 is 02:30 on Nov 10 in Moscow: belongs to Nov 10, not Nov 9.
        var lateUtc = new DateTimeOffset(2026, 11, 9, 23, 30, 0, TimeSpan.Zero);
        await CreateAsync(user, "Night owl", lateUtc, lateUtc.AddMinutes(30));

        var nov9 = await user.GetJsonAsync<PlannerRangeDto>("/api/v1/planner?from=2026-11-09&to=2026-11-09");
        var nov10 = await user.GetJsonAsync<PlannerRangeDto>("/api/v1/planner?from=2026-11-10&to=2026-11-10");

        Assert.Empty(nov9.Blocks);
        Assert.Single(nov10.Blocks);
        Assert.Single(nov10.TasksDue);
        Assert.Single(nov10.Habits);
        Assert.Equal("Europe/Moscow", nov10.TimeZoneId);
    }

    private static Task<TimeBlockSaveResult> CreateAsync(TestUser user, string title, DateTimeOffset start, DateTimeOffset end) =>
        user.PostJsonAsync<CreateTimeBlockRequest, TimeBlockSaveResult>("/api/v1/planner/blocks", new(title, null, start, end, null, null));
}
