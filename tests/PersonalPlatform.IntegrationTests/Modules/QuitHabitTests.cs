using System.Net;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Habits;
using PersonalPlatform.Contracts.Habits;
using PersonalPlatform.Contracts.Notes;
using PersonalPlatform.Contracts.Profile;

namespace PersonalPlatform.IntegrationTests.Modules;

public sealed class QuitHabitTests(ApiTestEnvironment env)
{
    [Fact]
    public async Task Days_without_the_habit_are_counted_from_the_quit_date_and_restart_after_a_relapse()
    {
        var user = await env.CreateUserAsync();
        var today = await TodayAsync(user);
        var created = await user.PostJsonAsync<CreateQuitHabitRequest, QuitHabitDetailDto>("/api/v1/quit-habits", new("Sweets", "Feel better", "#b5524b", today.AddDays(-20)));
        var id = created.Habit.Id;
        Assert.Equal(20, created.Habit.CurrentStreakDays);
        Assert.Equal("#B5524B", created.Habit.Color);
        Assert.Equal(30, created.Habit.NextMilestoneDays);

        await user.PostJsonAsync<LogRelapseRequest, QuitHabitDetailDto>($"/api/v1/quit-habits/{id}/relapses", new(today.AddDays(-8), "birthday party"), HttpStatusCode.OK);
        var detail = await user.PostJsonAsync<LogRelapseRequest, QuitHabitDetailDto>($"/api/v1/quit-habits/{id}/relapses", new(today.AddDays(-3), null), HttpStatusCode.OK);

        Assert.Equal(3, detail.Habit.CurrentStreakDays);
        Assert.Equal(12, detail.Habit.LongestStreakDays);
        Assert.Equal(2, detail.Habit.RelapseCount);
        Assert.Equal(today.AddDays(-3), detail.Habit.LastRelapse);
        Assert.Equal([today.AddDays(-3), today.AddDays(-8)], detail.Relapses.Select(r => r.Date));
        Assert.Equal("birthday party", detail.Relapses[1].Note);

        var undone = await user.DeleteAsync($"/api/v1/quit-habits/{id}/relapses/{today.AddDays(-3):yyyy-MM-dd}").EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal(8, (await undone.ReadJsonAsync<QuitHabitDetailDto>()).Habit.CurrentStreakDays);

        var list = await user.GetJsonAsync<List<QuitHabitDto>>("/api/v1/quit-habits");
        Assert.Equal(8, Assert.Single(list).CurrentStreakDays);
    }

    [Fact]
    public async Task Logging_the_same_day_twice_or_concurrently_records_one_relapse()
    {
        var user = await env.CreateUserAsync();
        var today = await TodayAsync(user);
        var habit = (await user.PostJsonAsync<CreateQuitHabitRequest, QuitHabitDetailDto>("/api/v1/quit-habits", new("Doom-scrolling", null, null, today.AddDays(-5)))).Habit;

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => user.PostAsync($"/api/v1/quit-habits/{habit.Id}/relapses", new LogRelapseRequest(today, null))));
        var detail = await user.PostJsonAsync<LogRelapseRequest, QuitHabitDetailDto>($"/api/v1/quit-habits/{habit.Id}/relapses", new(today, null), HttpStatusCode.OK);

        Assert.Equal(1, detail.Habit.RelapseCount);
        await using var db = env.Database.CreateOwnerContext();
        Assert.Equal(1, await db.QuitHabitRelapses.IgnoreQueryFilters().CountAsync(r => r.QuitHabitId == habit.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Invalid_dates_and_archived_habits_are_rejected()
    {
        var user = await env.CreateUserAsync();
        var today = await TodayAsync(user);

        Assert.Equal("date_in_future", await (await user.PostAsync("/api/v1/quit-habits", new CreateQuitHabitRequest("Soda", null, null, today.AddDays(1)))).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsync("/api/v1/quit-habits", new CreateQuitHabitRequest("", null, "red", null))).StatusCode);

        var habit = (await user.PostJsonAsync<CreateQuitHabitRequest, QuitHabitDetailDto>("/api/v1/quit-habits", new("Soda", null, null, null))).Habit;
        Assert.Equal(today, habit.QuitDate);
        Assert.Equal(0, habit.CurrentStreakDays);
        Assert.Equal("date_before_quit", await (await user.PostAsync($"/api/v1/quit-habits/{habit.Id}/relapses", new LogRelapseRequest(today.AddDays(-1), null))).ProblemCodeAsync());
        Assert.Equal("date_in_future", await (await user.PostAsync($"/api/v1/quit-habits/{habit.Id}/relapses", new LogRelapseRequest(today.AddDays(1), null))).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsync($"/api/v1/quit-habits/{habit.Id}/relapses", new LogRelapseRequest(today, new string('n', 501)))).StatusCode);

        var archived = await user.PostJsonAsync<SetArchivedRequest, QuitHabitDetailDto>($"/api/v1/quit-habits/{habit.Id}/archive", new(true), HttpStatusCode.OK);
        Assert.True(archived.Habit.IsArchived);
        Assert.Empty(await user.GetJsonAsync<List<QuitHabitDto>>("/api/v1/quit-habits"));
        Assert.Single(await user.GetJsonAsync<List<QuitHabitDto>>("/api/v1/quit-habits?includeArchived=true"));
        Assert.Equal("habit_archived", await (await user.PostAsync($"/api/v1/quit-habits/{habit.Id}/relapses", new LogRelapseRequest(today, null))).ProblemCodeAsync());
    }

    [Fact]
    public async Task Update_uses_optimistic_concurrency_and_delete_removes_the_history()
    {
        var user = await env.CreateUserAsync();
        var today = await TodayAsync(user);
        var habit = (await user.PostJsonAsync<CreateQuitHabitRequest, QuitHabitDetailDto>("/api/v1/quit-habits", new("Smoking", null, null, today.AddDays(-2)))).Habit;
        await user.PostJsonAsync<LogRelapseRequest, QuitHabitDetailDto>($"/api/v1/quit-habits/{habit.Id}/relapses", new(today.AddDays(-1), null), HttpStatusCode.OK);

        var updated = await user.PutJsonAsync<UpdateQuitHabitRequest, QuitHabitDetailDto>($"/api/v1/quit-habits/{habit.Id}", new("Cigarettes", "health", "#4f8a8b", today.AddDays(-10), habit.Version));
        Assert.Equal("Cigarettes", updated.Habit.Name);
        Assert.Equal(1, updated.Habit.CurrentStreakDays);
        Assert.Equal(9, updated.Habit.LongestStreakDays);

        var stale = await user.PutAsync($"/api/v1/quit-habits/{habit.Id}", new UpdateQuitHabitRequest("Stale", null, null, today, habit.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        await user.DeleteAsync($"/api/v1/quit-habits/{habit.Id}").EnsureStatusAsync(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/v1/quit-habits/{habit.Id}")).StatusCode);
        await using var db = env.Database.CreateOwnerContext();
        Assert.False(await db.QuitHabitRelapses.IgnoreQueryFilters().AnyAsync(r => r.QuitHabitId == habit.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Number_of_habits_to_quit_is_limited()
    {
        var user = await env.CreateUserAsync();
        for (var i = 0; i < QuitHabitService.MaxQuitHabitsPerUser; i++)
        {
            await user.PostAsync("/api/v1/quit-habits", new CreateQuitHabitRequest($"Habit {i}", null, null, null)).EnsureStatusAsync(HttpStatusCode.Created);
        }

        var over = await user.PostAsync("/api/v1/quit-habits", new CreateQuitHabitRequest("One too many", null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal("limit_reached", await over.ProblemCodeAsync());
    }

    private static async Task<DateOnly> TodayAsync(TestUser user) => (await user.GetJsonAsync<ProfileDto>("/api/v1/profile")).Today;
}
