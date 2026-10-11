using PersonalPlatform.Contracts.Organization;

namespace PersonalPlatform.Contracts.Tasks;

public enum TaskItemStatus
{
    Open = 0,
    InProgress = 1,
    Done = 2,
}

public enum TaskPriority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3,
}

/// <summary>Bit mask: Monday = 1, Tuesday = 2, ..., Sunday = 64.</summary>
[Flags]
#pragma warning disable CA1711
public enum Weekdays
#pragma warning restore CA1711
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
}

public enum TaskView
{
    All = 0,
    Today = 1,
    Upcoming = 2,
    Overdue = 3,
    Completed = 4,
    Unscheduled = 5,
}

public enum TaskSort
{
    DueDate = 0,
    Priority = 1,
    CreatedAt = 2,
    Title = 3,
}

public enum BulkTaskAction
{
    Complete = 0,
    Reopen = 1,
    Delete = 2,
    Reschedule = 3,
}

public sealed record RecurrenceDto(RecurrenceFrequency Frequency, int Interval, Weekdays Days, DateOnly StartDate, DateOnly? EndDate);

public sealed record SubtaskDto(Guid Id, string Title, bool IsDone, int Position);

public sealed record SubtaskInput(string Title, bool IsDone);

public sealed record TaskDto(
    Guid Id,
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateOnly? DueDate,
    TimeOnly? DueTime,
    DateTimeOffset? ReminderAt,
    Guid? CategoryId,
    IReadOnlyList<TagDto> Tags,
    IReadOnlyList<SubtaskDto> Subtasks,
    Guid? SeriesId,
    DateOnly? OccurrenceDate,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsOverdue,
    uint Version);

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    TaskPriority Priority,
    DateOnly? DueDate,
    TimeOnly? DueTime,
    DateTimeOffset? ReminderAt,
    Guid? CategoryId,
    IReadOnlyList<Guid>? TagIds,
    IReadOnlyList<SubtaskInput>? Subtasks,
    RecurrenceDto? Recurrence);

public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    TaskPriority Priority,
    DateOnly? DueDate,
    TimeOnly? DueTime,
    DateTimeOffset? ReminderAt,
    Guid? CategoryId,
    IReadOnlyList<Guid>? TagIds,
    IReadOnlyList<SubtaskInput>? Subtasks,
    uint Version);

public sealed record SetTaskStatusRequest(TaskItemStatus Status);

public sealed record SetSubtaskDoneRequest(bool IsDone);

public sealed record RescheduleTaskRequest(DateOnly? DueDate, TimeOnly? DueTime);

public sealed record BulkTaskRequest(IReadOnlyList<Guid> Ids, BulkTaskAction Action, DateOnly? DueDate);

public sealed record BulkTaskResult(int Affected);

public sealed record TaskQuery(
    TaskView View = TaskView.All,
    string? Search = null,
    TaskItemStatus? Status = null,
    TaskPriority? Priority = null,
    Guid? CategoryId = null,
    Guid? TagId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    TaskSort Sort = TaskSort.DueDate,
    bool Descending = false,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

public sealed record TaskSeriesDto(
    Guid Id,
    string Title,
    string? Description,
    TaskPriority Priority,
    Guid? CategoryId,
    TimeOnly? DueTime,
    RecurrenceDto Recurrence,
    bool IsActive,
    uint Version);

public sealed record UpdateTaskSeriesRequest(
    string Title,
    string? Description,
    TaskPriority Priority,
    Guid? CategoryId,
    TimeOnly? DueTime,
    RecurrenceDto Recurrence,
    uint Version);
