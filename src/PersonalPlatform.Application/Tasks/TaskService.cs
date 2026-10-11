using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Application.Organization;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Organization;
using PersonalPlatform.Contracts.Tasks;
using PersonalPlatform.Domain.Tasks;
using DomainScope = PersonalPlatform.Domain.Organization.CategoryScope;
using DomainStatus = PersonalPlatform.Domain.Tasks.TaskItemStatus;

namespace PersonalPlatform.Application.Tasks;

/// <summary>Task use cases. All reads and writes are restricted to the current owner.</summary>
public sealed class TaskService(IAppDbContext db, ICurrentUser currentUser, UserClock clock)
{
    private Guid Owner => currentUser.UserId;

    public async Task<PagedResult<TaskDto>> ListAsync(TaskQuery query, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var today = (await clock.GetAsync(cancellationToken)).Today;
        var tasks = db.Tasks.AsNoTracking().Where(t => t.OwnerId == owner);

        tasks = query.View switch
        {
            TaskView.Today => tasks.Where(t => t.DueDate == today),
            TaskView.Upcoming => tasks.Where(t => t.DueDate > today && t.Status != DomainStatus.Done),
            TaskView.Overdue => tasks.Where(t => t.DueDate < today && t.Status != DomainStatus.Done),
            TaskView.Completed => tasks.Where(t => t.Status == DomainStatus.Done),
            TaskView.Unscheduled => tasks.Where(t => t.DueDate == null && t.Status != DomainStatus.Done),
            _ => tasks,
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            tasks = tasks.Where(t => t.Title.ToLower().Contains(term) || (t.Description != null && t.Description.ToLower().Contains(term)));
        }

        if (query.Status is { } status)
        {
            var s = status.ToDomain();
            tasks = tasks.Where(t => t.Status == s);
        }

        if (query.Priority is { } priority)
        {
            var p = priority.ToDomain();
            tasks = tasks.Where(t => t.Priority == p);
        }

        if (query.CategoryId is { } categoryId)
        {
            tasks = tasks.Where(t => t.CategoryId == categoryId);
        }

        if (query.TagId is { } tagId)
        {
            tasks = tasks.Where(t => t.Tags.Any(tt => tt.TagId == tagId));
        }

        if (query.From is { } from)
        {
            tasks = tasks.Where(t => t.DueDate >= from);
        }

        if (query.To is { } to)
        {
            tasks = tasks.Where(t => t.DueDate <= to);
        }

        var ordered = Sort(tasks, query.View == TaskView.Completed ? TaskSort.CreatedAt : query.Sort, query.Descending || query.View == TaskView.Completed);
        return await ordered.Select(Projection(today)).ToPagedResultAsync(query.Page, query.PageSize, cancellationToken);
    }

    public async Task<TaskDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var today = (await clock.GetAsync(cancellationToken)).Today;
        return await db.Tasks.AsNoTracking()
            .Where(t => t.Id == id && t.OwnerId == owner)
            .Select(Projection(today))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException();
    }

    public async Task<TaskDto> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var now = clock.UtcNow;
        await OrganizationService.EnsureCategoryAsync(db, owner, request.CategoryId, DomainScope.Task, cancellationToken);
        var tags = await OrganizationService.LoadOwnedTagsAsync(db, owner, request.TagIds, cancellationToken);

        TaskItem task;
        if (request.Recurrence is { } recurrence)
        {
            var series = new TaskSeries(owner, now);
            series.SetTemplate(request.Title, request.Description, request.Priority.ToDomain(), request.CategoryId, request.DueTime, recurrence.ToDomain(), now);
            var first = series.FirstOccurrence()
                ?? throw new Domain.Common.DomainRuleException("invalid_recurrence", "The schedule has no occurrences.");
            task = series.CreateInstance(first, now);
            db.TaskSeries.Add(series);
        }
        else
        {
            task = new TaskItem(owner, now);
            task.SetDetails(request.Title, request.Description, request.Priority.ToDomain(), request.CategoryId, now);
            task.Schedule(request.DueDate, request.DueTime, request.ReminderAt, now);
        }

        task.ReplaceSubtasks((request.Subtasks ?? []).Select(s => (s.Title, s.IsDone)));
        task.ReplaceTags(tags.ToList());
        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(task.Id, cancellationToken);
    }

    public async Task<TaskDto> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var now = clock.UtcNow;
        var task = await LoadTrackedAsync(id, cancellationToken);
        db.SetExpectedVersion(task, request.Version);
        await OrganizationService.EnsureCategoryAsync(db, owner, request.CategoryId, DomainScope.Task, cancellationToken);
        var tags = await OrganizationService.LoadOwnedTagsAsync(db, owner, request.TagIds, cancellationToken);

        // Only this instance changes; its series (if any) and sibling instances are untouched.
        task.SetDetails(request.Title, request.Description, request.Priority.ToDomain(), request.CategoryId, now);
        task.Schedule(request.DueDate, request.DueTime, request.ReminderAt, now);
        task.ReplaceSubtasks((request.Subtasks ?? []).Select(s => (s.Title, s.IsDone)));
        task.ReplaceTags(tags.ToList());
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<TaskDto> SetStatusAsync(Guid id, Contracts.Tasks.TaskItemStatus status, CancellationToken cancellationToken)
    {
        var task = await LoadTrackedAsync(id, cancellationToken);
        await ApplyStatusAsync(task, status.ToDomain(), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<TaskDto> RescheduleAsync(Guid id, RescheduleTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await LoadTrackedAsync(id, cancellationToken);
        task.Schedule(request.DueDate, request.DueTime, task.ReminderAt, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<TaskDto> SetSubtaskDoneAsync(Guid taskId, Guid subtaskId, bool isDone, CancellationToken cancellationToken)
    {
        var task = await LoadTrackedAsync(taskId, cancellationToken);
        task.SetSubtaskDone(subtaskId, isDone, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(taskId, cancellationToken);
    }

    public async Task DismissReminderAsync(Guid id, CancellationToken cancellationToken)
    {
        var task = await LoadTrackedAsync(id, cancellationToken);
        task.DismissReminder(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Deletes a task. Time blocks planned for it are kept but unlinked.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var exists = await db.Tasks.AnyAsync(t => t.Id == id && t.OwnerId == owner, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException();
        }

        await DetachTimeBlocksAsync(owner, [id], cancellationToken);
        await db.Tasks.Where(t => t.Id == id && t.OwnerId == owner).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Applies one action to up to <see cref="QueryLimits.MaxBulkItems"/> of the caller's tasks atomically.</summary>
    public async Task<BulkTaskResult> BulkAsync(BulkTaskRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var ids = request.Ids.Distinct().ToList();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        if (request.Action == BulkTaskAction.Delete)
        {
            await DetachTimeBlocksAsync(owner, ids, cancellationToken);
            var deleted = await db.Tasks.Where(t => t.OwnerId == owner && ids.Contains(t.Id)).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new BulkTaskResult(deleted);
        }

        var tasks = await db.Tasks.Where(t => t.OwnerId == owner && ids.Contains(t.Id)).ToListAsync(cancellationToken);
        var now = clock.UtcNow;
        var completedSeriesInstances = new List<TaskItem>();
        foreach (var task in tasks)
        {
            switch (request.Action)
            {
                case BulkTaskAction.Complete:
                    if (!task.IsDone && task.SeriesId is not null)
                    {
                        completedSeriesInstances.Add(task);
                    }

                    task.SetStatus(DomainStatus.Done, now);
                    break;
                case BulkTaskAction.Reopen:
                    task.SetStatus(DomainStatus.Open, now);
                    break;
                case BulkTaskAction.Reschedule:
                    task.Schedule(request.DueDate, request.DueDate is null ? null : task.DueTime, task.ReminderAt, now);
                    break;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var task in completedSeriesInstances)
        {
            await MaterialiseNextOccurrenceAsync(task, cancellationToken);
        }

        return new BulkTaskResult(tasks.Count);
    }

    public async Task<TaskSeriesDto> GetSeriesAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var series = await db.TaskSeries.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
        return ToDto(series);
    }

    /// <summary>Changes the template for future instances only; existing instances keep their data.</summary>
    public async Task<TaskSeriesDto> UpdateSeriesAsync(Guid id, UpdateTaskSeriesRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var series = await db.TaskSeries.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
        db.SetExpectedVersion(series, request.Version);
        await OrganizationService.EnsureCategoryAsync(db, owner, request.CategoryId, DomainScope.Task, cancellationToken);
        series.SetTemplate(request.Title, request.Description, request.Priority.ToDomain(), request.CategoryId, request.DueTime, request.Recurrence.ToDomain(), clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(series);
    }

    /// <summary>Stops generating new instances. Existing instances are kept.</summary>
    public async Task<TaskSeriesDto> StopSeriesAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var series = await db.TaskSeries.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
        series.Stop(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(series);
    }

    /// <summary>Deletes the series. Its instances remain as ordinary, non-recurring tasks.</summary>
    public async Task DeleteSeriesAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        if (!await db.TaskSeries.AnyAsync(s => s.Id == id && s.OwnerId == owner, cancellationToken))
        {
            throw new NotFoundException();
        }

        await db.Tasks.Where(t => t.OwnerId == owner && t.SeriesId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.SeriesId, (Guid?)null).SetProperty(t => t.OccurrenceDate, (DateOnly?)null), cancellationToken);
        await db.TaskSeries.Where(s => s.Id == id && s.OwnerId == owner).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ApplyStatusAsync(TaskItem task, DomainStatus status, CancellationToken cancellationToken)
    {
        var completingInstance = status == DomainStatus.Done && !task.IsDone && task.SeriesId is not null;
        task.SetStatus(status, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        if (completingInstance)
        {
            await MaterialiseNextOccurrenceAsync(task, cancellationToken);
        }
    }

    /// <summary>
    /// Creates the next instance of a recurring task after one was completed. Idempotent: the unique index on
    /// (series_id, occurrence_date) turns a concurrent or repeated attempt into a no-op.
    /// </summary>
    private async Task MaterialiseNextOccurrenceAsync(TaskItem completed, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var series = await db.TaskSeries.FirstOrDefaultAsync(s => s.Id == completed.SeriesId && s.OwnerId == owner, cancellationToken);
        if (series is null || completed.OccurrenceDate is not { } occurrence)
        {
            return;
        }

        var today = (await clock.GetAsync(cancellationToken)).Today;
        if (series.NextOccurrenceAfter(occurrence, today) is not { } next
            || await db.Tasks.AnyAsync(t => t.SeriesId == series.Id && t.OccurrenceDate == next, cancellationToken))
        {
            return;
        }

        db.Tasks.Add(series.CreateInstance(next, clock.UtcNow));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            db.ResetTracking(); // a concurrent completion already created it
        }
    }

    private async Task DetachTimeBlocksAsync(Guid owner, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        await db.TimeBlocks.Where(b => b.OwnerId == owner && b.TaskId != null && taskIds.Contains(b.TaskId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.TaskId, (Guid?)null), cancellationToken);

    private async Task<TaskItem> LoadTrackedAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        return await db.Tasks
            .Include(t => t.Subtasks)
            .Include(t => t.Tags)
            .FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
    }

    private static IOrderedQueryable<TaskItem> Sort(IQueryable<TaskItem> tasks, TaskSort sort, bool descending)
    {
        IOrderedQueryable<TaskItem> ordered = (sort, descending) switch
        {
            (TaskSort.Priority, false) => tasks.OrderBy(t => t.Priority),
            (TaskSort.Priority, true) => tasks.OrderByDescending(t => t.Priority),
            (TaskSort.CreatedAt, false) => tasks.OrderBy(t => t.CreatedAt),
            (TaskSort.CreatedAt, true) => tasks.OrderByDescending(t => t.CreatedAt),
            (TaskSort.Title, false) => tasks.OrderBy(t => t.Title),
            (TaskSort.Title, true) => tasks.OrderByDescending(t => t.Title),
            // Due date: undated tasks last, then by time, then by priority (highest first).
            (_, false) => tasks.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenBy(t => t.DueTime).ThenByDescending(t => t.Priority),
            (_, true) => tasks.OrderBy(t => t.DueDate == null).ThenByDescending(t => t.DueDate).ThenByDescending(t => t.DueTime),
        };

        // Stable tiebreaker so that pagination never skips or repeats rows.
        return ordered.ThenBy(t => t.Id);
    }

    private Expression<Func<TaskItem, TaskDto>> Projection(DateOnly today)
    {
        var tags = db.Tags;
        return t => new TaskDto(
            t.Id,
            t.Title,
            t.Description,
            (Contracts.Tasks.TaskItemStatus)t.Status,
            (Contracts.Tasks.TaskPriority)t.Priority,
            t.DueDate,
            t.DueTime,
            t.ReminderAt,
            t.CategoryId,
            tags.Where(tag => t.Tags.Select(tt => tt.TagId).Contains(tag.Id)).OrderBy(tag => tag.NormalizedName).Select(tag => new TagDto(tag.Id, tag.Name)).ToList(),
            t.Subtasks.OrderBy(s => s.Position).Select(s => new SubtaskDto(s.Id, s.Title, s.IsDone, s.Position)).ToList(),
            t.SeriesId,
            t.OccurrenceDate,
            t.CompletedAt,
            t.CreatedAt,
            t.UpdatedAt,
            t.Status != DomainStatus.Done && t.DueDate < today,
            t.Version);
    }

    private static TaskSeriesDto ToDto(TaskSeries s) => new(
        s.Id, s.Title, s.Description, s.Priority.ToContract(), s.CategoryId, s.DueTime, s.Pattern.ToContract(), s.IsActive, s.Version);
}
