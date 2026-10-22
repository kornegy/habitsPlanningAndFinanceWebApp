using System.Net;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Contracts.Habits;
using PersonalPlatform.Contracts.Notes;
using PersonalPlatform.Contracts.Profile;
using PersonalPlatform.Contracts.Tasks;

namespace PersonalPlatform.IntegrationTests.Modules;

public sealed class HabitTests(ApiTestEnvironment env)
{
    [Fact]
    public async Task Daily_habit_streaks_and_completion_flags_come_from_stored_completions()
    {
        var user = await env.CreateUserAsync();
        var today = await TodayAsync(user);
        var habit = await CreateDailyAsync(user, today.AddDays(-10));

        foreach (var offset in new[] { 5, 3, 2, 1 })
        {
            await user.PutAsync($"/api/v1/habits/{habit.Id}/completions/{today.AddDays(-offset):yyyy-MM-dd}", new { }).EnsureStatusAsync(HttpStatusCode.OK);
        }

        var dto = await user.GetJsonAsync<HabitDto>($"/api/v1/habits/{habit.Id}");
        Assert.Equal(3, dto.CurrentStreak); // today pending does not break the streak
        Assert.Equal(3, dto.LongestStreak);
        Assert.False(dto.CompletedToday);
        Assert.True(dto.IsDueToday);

        var result = await user.PutAsync($"/api/v1/habits/{habit.Id}/completions/{today:yyyy-MM-dd}", new { }).EnsureStatusAsync(HttpStatusCode.OK);
        var completion = await result.ReadJsonAsync<HabitCompletionResult>();
        Assert.True(completion.Completed);
        Assert.Equal(4, completion.CurrentStreak);

        var undone = await user.DeleteAsync($"/api/v1/habits/{habit.Id}/completions/{today:yyyy-MM-dd}").EnsureStatusAsync(HttpStatusCode.OK);
        Assert.False((await undone.ReadJsonAsync<HabitCompletionResult>()).Completed);
    }

    [Fact]
    public async Task Checking_off_the_same_date_twice_or_concurrently_records_one_completion()
    {
        var user = await env.CreateUserAsync();
        var today = await TodayAsync(user);
        var habit = await CreateDailyAsync(user, today);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => user.PutAsync($"/api/v1/habits/{habit.Id}/completions/{today:yyyy-MM-dd}", new { })));
        await user.PutAsync($"/api/v1/habits/{habit.Id}/completions/{today:yyyy-MM-dd}", new { }).EnsureStatusAsync(HttpStatusCode.OK);

        await using var db = env.Database.CreateOwnerContext();
        Assert.Equal(1, await db.HabitCompletions.IgnoreQueryFilters().CountAsync(c => c.HabitId == habit.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Future_unscheduled_and_pre_start_dates_cannot_be_checked_off()
    {
        var user = await env.CreateUserAsync();
        var today = await TodayAsync(user);
        var mondayOnly = await user.PostJsonAsync<CreateHabitRequest, HabitDto>("/api/v1/habits", new(
            "Mondays", null, null, null, HabitScheduleType.SpecificWeekdays, Weekdays.Monday, 1, today.AddDays(-14), null, null));
        var notMonday = Enumerable.Range(1, 7).Select(i => today.AddDays(-i)).First(d => d.DayOfWeek != DayOfWeek.Monday);

        var future = await user.PutAsync($"/api/v1/habits/{mondayOnly.Id}/completions/{today.AddDays(1):yyyy-MM-dd}", new { });
        var unscheduled = await user.PutAsync($"/api/v1/habits/{mondayOnly.Id}/completions/{notMonday:yyyy-MM-dd}", new { });
        var beforeStart = await user.PutAsync($"/api/v1/habits/{mondayOnly.Id}/completions/{today.AddDays(-30):yyyy-MM-dd}", new { });

        Assert.Equal("date_in_future", await future.ProblemCodeAsync());
        Assert.Equal("date_not_scheduled", await unscheduled.ProblemCodeAsync());
        Assert.Equal("date_before_start", await beforeStart.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, future.StatusCode);
    }

    [Fact]
    public async Task Every_n_days_schedule_history_and_stats()
    {
        var user = await env.CreateUserAsync();
        var today = await TodayAsync(user);
        var start = today.AddDays(-20);
        var habit = await user.PostJsonAsync<CreateHabitRequest, HabitDto>("/api/v1/habits", new(
            "Every 3 days", null, "#225588", null, HabitScheduleType.EveryNDays, Weekdays.None, 3, start, 10, new TimeOnly(8, 0)));
        await user.PutAsync($"/api/v1/habits/{habit.Id}/completions/{start:yyyy-MM-dd}", new { }).EnsureStatusAsync(HttpStatusCode.OK);
        await user.PutAsync($"/api/v1/habits/{habit.Id}/completions/{start.AddDays(3):yyyy-MM-dd}", new { }).EnsureStatusAsync(HttpStatusCode.OK);

        var history = await user.GetJsonAsync<HabitHistoryDto>($"/api/v1/habits/{habit.Id}/history?from={start:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.All(history.ScheduledDates, d => Assert.Equal(0, (d.DayNumber - start.DayNumber) % 3));
        Assert.Equal(2, history.CompletedDates.Count);

        var stats = await user.GetJsonAsync<HabitStatsDto>($"/api/v1/habits/{habit.Id}/stats?from={start:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal(2, stats.Completed);
        Assert.True(stats.Scheduled >= 6);
        Assert.Equal(decimal.Round(2m * 100 / stats.Scheduled, 1, MidpointRounding.AwayFromZero), stats.CompletionRate);
        Assert.NotEmpty(stats.Weeks);
        Assert.NotEmpty(stats.Months);

        var tooLong = await user.GetAsync($"/api/v1/habits/{habit.Id}/stats?from=2020-01-01&to=2026-01-01");
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Archive_restore_update_and_delete()
    {
        var user = await env.CreateUserAsync();
        var today = await TodayAsync(user);
        var habit = await CreateDailyAsync(user, today);

        var archived = await user.PostJsonAsync<SetArchivedRequest, HabitDto>($"/api/v1/habits/{habit.Id}/archive", new(true), HttpStatusCode.OK);
        Assert.True(archived.IsArchived);
        Assert.Empty(await user.GetJsonAsync<List<HabitDto>>("/api/v1/habits"));
        Assert.Single(await user.GetJsonAsync<List<HabitDto>>("/api/v1/habits?includeArchived=true"));
        Assert.Equal("habit_archived", await (await user.PutAsync($"/api/v1/habits/{habit.Id}/completions/{today:yyyy-MM-dd}", new { })).ProblemCodeAsync());

        var restored = await user.PostJsonAsync<SetArchivedRequest, HabitDto>($"/api/v1/habits/{habit.Id}/archive", new(false), HttpStatusCode.OK);
        var updated = await user.PutJsonAsync<UpdateHabitRequest, HabitDto>($"/api/v1/habits/{habit.Id}", new(
            "Read 20 pages", "books", "#112233", null, HabitScheduleType.SpecificWeekdays, Weekdays.Monday | Weekdays.Friday, 1, today, 30, null, restored.Version));
        Assert.Equal("Read 20 pages", updated.Name);
        Assert.Equal(Weekdays.Monday | Weekdays.Friday, updated.Days);

        var invalid = await user.PutAsync($"/api/v1/habits/{habit.Id}", new UpdateHabitRequest(
            "x", null, "red", null, HabitScheduleType.SpecificWeekdays, Weekdays.None, 1, today, null, null, updated.Version));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        await user.DeleteAsync($"/api/v1/habits/{habit.Id}").EnsureStatusAsync(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/v1/habits/{habit.Id}")).StatusCode);
    }

    [Fact]
    public async Task Reset_removes_check_offs_and_restarts_active_habits_from_today()
    {
        var user = await env.CreateUserAsync();
        var today = await TodayAsync(user);
        var first = await CreateDailyAsync(user, today.AddDays(-10));
        var second = await CreateDailyAsync(user, today.AddDays(-10));
        var archived = await CreateDailyAsync(user, today.AddDays(-10));
        foreach (var habit in new[] { first, second, archived })
        {
            foreach (var offset in new[] { 2, 1, 0 })
            {
                await user.PutAsync($"/api/v1/habits/{habit.Id}/completions/{today.AddDays(-offset):yyyy-MM-dd}", new { }).EnsureStatusAsync(HttpStatusCode.OK);
            }
        }

        await user.PostAsync($"/api/v1/habits/{archived.Id}/archive", new SetArchivedRequest(true)).EnsureStatusAsync(HttpStatusCode.OK);

        var single = await user.PostJsonAsync<object, HabitResetResult>($"/api/v1/habits/{first.Id}/reset", new { }, HttpStatusCode.OK);
        Assert.Equal(new HabitResetResult(1, 3), single);
        var afterSingle = await user.GetJsonAsync<HabitDto>($"/api/v1/habits/{first.Id}");
        Assert.Equal((today, 0, false), (afterSingle.StartDate, afterSingle.CurrentStreak, afterSingle.CompletedToday));
        Assert.Equal(3, (await user.GetJsonAsync<HabitDto>($"/api/v1/habits/{second.Id}")).CurrentStreak);
        Assert.Equal("date_before_start", await (await user.PutAsync($"/api/v1/habits/{first.Id}/completions/{today.AddDays(-1):yyyy-MM-dd}", new { })).ProblemCodeAsync());

        var all = await user.PostJsonAsync<object, HabitResetResult>("/api/v1/habits/reset", new { }, HttpStatusCode.OK);
        Assert.Equal(new HabitResetResult(2, 3), all); // first (already empty) and second; the archived habit keeps its history
        var stats = await user.GetJsonAsync<HabitStatsDto>($"/api/v1/habits/{second.Id}/stats?from={today.AddDays(-10):yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal((0, 0, 0), (stats.Completed, stats.Scheduled, stats.CurrentStreak)); // days before the reset are no longer counted as missed
        Assert.Equal(3, (await user.GetJsonAsync<HabitDto>($"/api/v1/habits/{archived.Id}")).LongestStreak);

        Assert.Equal(HttpStatusCode.NotFound, (await user.PostAsync($"/api/v1/habits/{Guid.NewGuid()}/reset", new { })).StatusCode);
    }

    private static Task<HabitDto> CreateDailyAsync(TestUser user, DateOnly start) =>
        user.PostJsonAsync<CreateHabitRequest, HabitDto>("/api/v1/habits", new("Read", null, null, null, HabitScheduleType.Daily, Weekdays.None, 1, start, null, null));

    private static async Task<DateOnly> TodayAsync(TestUser user) => (await user.GetJsonAsync<ProfileDto>("/api/v1/profile")).Today;
}
