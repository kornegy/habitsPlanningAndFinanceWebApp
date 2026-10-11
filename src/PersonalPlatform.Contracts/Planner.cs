using PersonalPlatform.Contracts.Tasks;

namespace PersonalPlatform.Contracts.Planner;

public sealed record TimeBlockDto(
    Guid Id,
    string Title,
    string? Notes,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid? TaskId,
    string? TaskTitle,
    bool TaskDone,
    uint Version);

/// <param name="NewTaskTitle">When set (and <paramref name="TaskId"/> is null), a task with this title is created
/// atomically together with the block ("quick task from calendar").</param>
public sealed record CreateTimeBlockRequest(
    string Title,
    string? Notes,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid? TaskId,
    string? NewTaskTitle);

public sealed record UpdateTimeBlockRequest(
    string Title,
    string? Notes,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid? TaskId,
    uint Version);

public sealed record MoveTimeBlockRequest(DateTimeOffset StartsAt, DateTimeOffset EndsAt, uint Version);

public sealed record ConflictDto(Guid Id, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>Saved block plus the blocks it overlaps (reported, not rejected, when overlaps are allowed).</summary>
public sealed record TimeBlockSaveResult(TimeBlockDto Block, IReadOnlyList<ConflictDto> Conflicts);

public sealed record PlannerHabitDto(Guid HabitId, string Name, string Color, DateOnly Date, bool Completed);

public sealed record PlannerTaskDto(Guid Id, string Title, DateOnly? DueDate, TimeOnly? DueTime, bool IsDone, TaskPriority Priority);

public sealed record PlannerRangeDto(
    DateOnly From,
    DateOnly To,
    string TimeZoneId,
    IReadOnlyList<TimeBlockDto> Blocks,
    IReadOnlyList<PlannerHabitDto> Habits,
    IReadOnlyList<PlannerTaskDto> TasksDue,
    IReadOnlyList<ConflictPairDto> Conflicts);

public sealed record ConflictPairDto(Guid FirstId, Guid SecondId);
