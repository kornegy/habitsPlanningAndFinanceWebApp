using System.Net;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Organization;
using PersonalPlatform.Contracts.Planner;
using PersonalPlatform.Contracts.Profile;
using PersonalPlatform.Contracts.Tasks;

namespace PersonalPlatform.IntegrationTests.Modules;

public sealed class TaskTests(ApiTestEnvironment env)
{
    [Fact]
    public async Task Create_read_update_delete_with_subtasks_and_tags()
    {
        var user = await env.CreateUserAsync();
        var tag = await user.PostJsonAsync<CreateTagRequest, TagDto>("/api/v1/tags", new("Work"), HttpStatusCode.OK);
        var category = await user.PostJsonAsync<CreateCategoryRequest, CategoryDto>("/api/v1/categories", new(CategoryScope.Task, "Home", "#336699"));

        var created = await user.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new(
            "Write report", "Quarterly", TaskPriority.High, new DateOnly(2026, 12, 1), new TimeOnly(9, 30), null, category.Id, [tag.Id],
            [new("Outline", false), new("Draft", false)], null));

        Assert.Equal("Write report", created.Title);
        Assert.Equal(2, created.Subtasks.Count);
        Assert.Equal("Work", Assert.Single(created.Tags).Name);
        Assert.Equal(category.Id, created.CategoryId);

        var updated = await user.PutJsonAsync<UpdateTaskRequest, TaskDto>($"/api/v1/tasks/{created.Id}", new(
            "Write final report", null, TaskPriority.Medium, null, null, null, null, [], [new("Only one", true)], created.Version));
        Assert.Equal("Write final report", updated.Title);
        Assert.Null(updated.DueDate);
        Assert.Empty(updated.Tags);
        Assert.True(Assert.Single(updated.Subtasks).IsDone);

        var stale = await user.PutAsync($"/api/v1/tasks/{created.Id}", new UpdateTaskRequest("Stale", null, TaskPriority.None, null, null, null, null, null, null, created.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency_conflict", await stale.ProblemCodeAsync());

        await user.DeleteAsync($"/api/v1/tasks/{created.Id}").EnsureStatusAsync(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/v1/tasks/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Views_select_today_overdue_upcoming_completed_and_unscheduled_tasks()
    {
        var user = await env.CreateUserAsync();
        var today = (await user.GetJsonAsync<ProfileDto>("/api/v1/profile")).Today;
        var overdue = await CreateAsync(user, "overdue", today.AddDays(-2));
        var dueToday = await CreateAsync(user, "today", today);
        var upcoming = await CreateAsync(user, "upcoming", today.AddDays(3));
        var unscheduled = await CreateAsync(user, "someday", null);
        var done = await CreateAsync(user, "done", today.AddDays(-1));
        await user.PostAsync($"/api/v1/tasks/{done.Id}/status", new SetTaskStatusRequest(TaskItemStatus.Done)).EnsureStatusAsync(HttpStatusCode.OK);

        Assert.Equal([dueToday.Id], await IdsAsync(user, "view=Today"));
        Assert.Equal([overdue.Id], await IdsAsync(user, "view=Overdue"));
        Assert.Equal([upcoming.Id], await IdsAsync(user, "view=Upcoming"));
        Assert.Equal([done.Id], await IdsAsync(user, "view=Completed"));
        Assert.Equal([unscheduled.Id], await IdsAsync(user, "view=Unscheduled"));
        Assert.Equal(5, (await IdsAsync(user, "view=All")).Count);

        var overdueDto = await user.GetJsonAsync<TaskDto>($"/api/v1/tasks/{overdue.Id}");
        Assert.True(overdueDto.IsOverdue);
    }

    [Fact]
    public async Task Search_filter_sort_and_bounded_pagination()
    {
        var user = await env.CreateUserAsync();
        var tag = await user.PostJsonAsync<CreateTagRequest, TagDto>("/api/v1/tags", new("errands"), HttpStatusCode.OK);
        for (var i = 0; i < 30; i++)
        {
            await user.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new(
                $"Item {i:00}", i % 2 == 0 ? "buy milk" : null, (TaskPriority)(i % 4), null, null, null, null, i < 5 ? [tag.Id] : null, null, null));
        }

        var page = await user.GetJsonAsync<PagedResult<TaskDto>>("/api/v1/tasks?search=MILK&pageSize=10&page=2&sort=Title");
        Assert.Equal(15, page.TotalCount);
        Assert.Equal(10, page.PageSize);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(["Item 20", "Item 22", "Item 24", "Item 26", "Item 28"], page.Items.Select(t => t.Title));

        var tagged = await user.GetJsonAsync<PagedResult<TaskDto>>($"/api/v1/tasks?tagId={tag.Id}");
        Assert.Equal(5, tagged.TotalCount);

        var high = await user.GetJsonAsync<PagedResult<TaskDto>>("/api/v1/tasks?priority=High&pageSize=100");
        Assert.All(high.Items, t => Assert.Equal(TaskPriority.High, t.Priority));

        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync("/api/v1/tasks?pageSize=1000000000")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync("/api/v1/tasks?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync($"/api/v1/tasks?search={new string('x', 500)}")).StatusCode);
    }

    [Fact]
    public async Task Completing_a_recurring_instance_creates_the_next_one_exactly_once()
    {
        var user = await env.CreateUserAsync();
        var today = (await user.GetJsonAsync<ProfileDto>("/api/v1/profile")).Today;
        var first = await user.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new(
            "Water plants", null, TaskPriority.Low, null, null, null, null, null, null,
            new RecurrenceDto(RecurrenceFrequency.Daily, 2, Weekdays.None, today, null)));
        Assert.Equal(today, first.DueDate);
        Assert.NotNull(first.SeriesId);

        await SetStatusAsync(user, first.Id, TaskItemStatus.Done);
        await SetStatusAsync(user, first.Id, TaskItemStatus.Open);
        await SetStatusAsync(user, first.Id, TaskItemStatus.Done);

        var instances = (await user.GetJsonAsync<PagedResult<TaskDto>>("/api/v1/tasks?pageSize=100")).Items
            .Where(t => t.SeriesId == first.SeriesId).ToList();
        Assert.Equal(2, instances.Count);
        Assert.Contains(instances, t => t.DueDate == today.AddDays(2) && t.Status == TaskItemStatus.Open);
    }

    [Fact]
    public async Task Concurrent_completion_of_a_recurring_instance_does_not_duplicate_the_next_occurrence()
    {
        var user = await env.CreateUserAsync();
        var today = (await user.GetJsonAsync<ProfileDto>("/api/v1/profile")).Today;
        var first = await user.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new(
            "Daily review", null, TaskPriority.None, null, null, null, null, null, null,
            new RecurrenceDto(RecurrenceFrequency.Daily, 1, Weekdays.None, today, null)));

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => user.PostAsync($"/api/v1/tasks/{first.Id}/status", new SetTaskStatusRequest(TaskItemStatus.Done))));

        var instances = (await user.GetJsonAsync<PagedResult<TaskDto>>("/api/v1/tasks?pageSize=100")).Items.Where(t => t.SeriesId == first.SeriesId).ToList();
        Assert.Equal(2, instances.Count);
    }

    [Fact]
    public async Task Editing_an_instance_does_not_change_the_series_and_series_edits_apply_to_future_instances()
    {
        var user = await env.CreateUserAsync();
        var today = (await user.GetJsonAsync<ProfileDto>("/api/v1/profile")).Today;
        var first = await user.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new(
            "Gym", null, TaskPriority.Medium, null, null, null, null, null, null,
            new RecurrenceDto(RecurrenceFrequency.Weekly, 1, Weekdays.None, today, null)));

        await user.PutJsonAsync<UpdateTaskRequest, TaskDto>($"/api/v1/tasks/{first.Id}", new(
            "Gym (moved)", null, TaskPriority.Medium, today.AddDays(1), null, null, null, null, null, first.Version));
        var series = await user.GetJsonAsync<TaskSeriesDto>($"/api/v1/task-series/{first.SeriesId}");
        Assert.Equal("Gym", series.Title);

        await user.PutJsonAsync<UpdateTaskSeriesRequest, TaskSeriesDto>($"/api/v1/task-series/{series.Id}", new(
            "Gym session", null, TaskPriority.High, null, new TimeOnly(18, 0), series.Recurrence, series.Version));
        await SetStatusAsync(user, first.Id, TaskItemStatus.Done);

        var tasks = (await user.GetJsonAsync<PagedResult<TaskDto>>("/api/v1/tasks?pageSize=100")).Items;
        Assert.Contains(tasks, t => t.Id == first.Id && t.Title == "Gym (moved)");
        var next = Assert.Single(tasks, t => t.SeriesId == series.Id && t.Id != first.Id);
        Assert.Equal("Gym session", next.Title);
        Assert.Equal(TaskPriority.High, next.Priority);
        Assert.Equal(today.AddDays(7), next.DueDate);
    }

    [Fact]
    public async Task Bulk_operations_are_bounded_and_affect_only_own_tasks()
    {
        var user = await env.CreateUserAsync();
        var other = await env.CreateUserAsync();
        var mine = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            mine.Add((await CreateAsync(user, $"t{i}", null)).Id);
        }

        var foreign = await CreateAsync(other, "not yours", null);

        var result = await user.PostJsonAsync<BulkTaskRequest, BulkTaskResult>("/api/v1/tasks/bulk", new([.. mine, foreign.Id], BulkTaskAction.Complete, null), HttpStatusCode.OK);
        Assert.Equal(3, result.Affected);
        Assert.Equal(TaskItemStatus.Open, (await other.GetJsonAsync<TaskDto>($"/api/v1/tasks/{foreign.Id}")).Status);

        var tooMany = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList();
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsync("/api/v1/tasks/bulk", new BulkTaskRequest(tooMany, BulkTaskAction.Delete, null))).StatusCode);

        var deleted = await user.PostJsonAsync<BulkTaskRequest, BulkTaskResult>("/api/v1/tasks/bulk", new([.. mine, foreign.Id], BulkTaskAction.Delete, null), HttpStatusCode.OK);
        Assert.Equal(3, deleted.Affected);
        await other.GetAsync($"/api/v1/tasks/{foreign.Id}").EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleting_a_task_keeps_its_time_blocks_but_unlinks_them()
    {
        var user = await env.CreateUserAsync();
        var task = await CreateAsync(user, "planned", null);
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var block = await user.PostJsonAsync<CreateTimeBlockRequest, TimeBlockSaveResult>("/api/v1/planner/blocks", new("Focus", null, start, start.AddHours(1), task.Id, null));

        await user.DeleteAsync($"/api/v1/tasks/{task.Id}").EnsureStatusAsync(HttpStatusCode.NoContent);

        var reloaded = await user.GetJsonAsync<TimeBlockDto>($"/api/v1/planner/blocks/{block.Block.Id}");
        Assert.Null(reloaded.TaskId);
    }

    [Fact]
    public async Task Deleting_a_category_uncategorises_its_tasks()
    {
        var user = await env.CreateUserAsync();
        var category = await user.PostJsonAsync<CreateCategoryRequest, CategoryDto>("/api/v1/categories", new(CategoryScope.Task, "Temp", null));
        var task = await user.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new("x", null, TaskPriority.None, null, null, null, category.Id, null, null, null));

        await user.DeleteAsync($"/api/v1/categories/{category.Id}").EnsureStatusAsync(HttpStatusCode.NoContent);

        Assert.Null((await user.GetJsonAsync<TaskDto>($"/api/v1/tasks/{task.Id}")).CategoryId);
        var duplicate = await user.PostAsync("/api/v1/categories", new CreateCategoryRequest(CategoryScope.Task, "Same", null));
        var again = await user.PostAsync("/api/v1/categories", new CreateCategoryRequest(CategoryScope.Task, "Same", null));
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Invalid_input_is_rejected_with_validation_problems()
    {
        var user = await env.CreateUserAsync();

        var empty = await user.PostAsync("/api/v1/tasks", new CreateTaskRequest("", null, TaskPriority.None, null, null, null, null, null, null, null));
        var timeWithoutDate = await user.PostAsync("/api/v1/tasks", new CreateTaskRequest("x", null, TaskPriority.None, null, new TimeOnly(1, 0), null, null, null, null, null));
        var badInterval = await user.PostAsync("/api/v1/tasks", new CreateTaskRequest("x", null, TaskPriority.None, null, null, null, null, null, null, new RecurrenceDto(RecurrenceFrequency.Daily, 0, Weekdays.None, new DateOnly(2026, 1, 1), null)));
        var tooLong = await user.PostAsync("/api/v1/tasks", new CreateTaskRequest(new string('x', 201), null, TaskPriority.None, null, null, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, timeWithoutDate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badInterval.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    private static Task<TaskDto> CreateAsync(TestUser user, string title, DateOnly? due) =>
        user.PostJsonAsync<CreateTaskRequest, TaskDto>("/api/v1/tasks", new(title, null, TaskPriority.None, due, null, null, null, null, null, null));

    private static async Task SetStatusAsync(TestUser user, Guid id, TaskItemStatus status) =>
        await user.PostAsync($"/api/v1/tasks/{id}/status", new SetTaskStatusRequest(status)).EnsureStatusAsync(HttpStatusCode.OK);

    private static async Task<List<Guid>> IdsAsync(TestUser user, string query) =>
        (await user.GetJsonAsync<PagedResult<TaskDto>>($"/api/v1/tasks?{query}&pageSize=100")).Items.Select(t => t.Id).ToList();
}
