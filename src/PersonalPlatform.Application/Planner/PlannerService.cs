using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Planner;
using PersonalPlatform.Domain.Planner;
using PersonalPlatform.Domain.Tasks;
using PersonalPlatform.Domain.Users;

namespace PersonalPlatform.Application.Planner;

/// <summary>
/// Day/week planner. Blocks are stored as UTC instants; calendar days are interpreted in the owner's time zone.
/// </summary>
/// <remarks>
/// Overlap rule: when the owner's profile allows overlapping blocks (default), overlaps are saved and reported
/// back as conflicts. When it does not, a save that would overlap is rejected with 409. To make that check
/// race-free, saves take a per-user advisory lock inside the transaction.
/// </remarks>
public sealed class PlannerService(IAppDbContext db, ICurrentUser currentUser, UserClock clock)
{
    private const string LockScope = "planner";

    private Guid Owner => currentUser.UserId;

    public async Task<PlannerRangeDto> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var context = await clock.GetAsync(cancellationToken);
        var rangeStart = TimeZones.StartOfDayUtc(from, context.Zone);
        var rangeEnd = TimeZones.StartOfDayUtc(to.AddDays(1), context.Zone);

        var blocks = await ProjectBlocks(owner, db.TimeBlocks.Where(b => b.StartsAt < rangeEnd && b.EndsAt > rangeStart))
            .ToListAsync(cancellationToken);

        var habits = await db.Habits.AsNoTracking()
            .Where(h => h.OwnerId == owner && h.ArchivedAt == null && h.StartDate <= to)
            .ToListAsync(cancellationToken);
        var completions = await db.HabitCompletions.AsNoTracking()
            .Where(c => c.OwnerId == owner && c.Date >= from && c.Date <= to)
            .Select(c => new { c.HabitId, c.Date })
            .ToListAsync(cancellationToken);
        var completed = completions.Select(c => (c.HabitId, c.Date)).ToHashSet();
        var plannerHabits = new List<PlannerHabitDto>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            plannerHabits.AddRange(habits
                .Where(h => h.Schedule.IsDue(date, h.StartDate))
                .Select(h => new PlannerHabitDto(h.Id, h.Name, h.Color, date, completed.Contains((h.Id, date)))));
        }

        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.OwnerId == owner && t.DueDate >= from && t.DueDate <= to)
            .OrderBy(t => t.DueDate).ThenBy(t => t.DueTime).ThenByDescending(t => t.Priority)
            .Take(500)
            .Select(t => new PlannerTaskDto(t.Id, t.Title, t.DueDate, t.DueTime, t.Status == TaskItemStatus.Done, (Contracts.Tasks.TaskPriority)t.Priority))
            .ToListAsync(cancellationToken);

        return new PlannerRangeDto(from, to, context.Profile.TimeZoneId, blocks, plannerHabits, tasks, ConflictPairs(blocks));
    }

    public async Task<TimeBlockSaveResult> CreateAsync(CreateTimeBlockRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var context = await clock.GetAsync(cancellationToken);
        var now = clock.UtcNow;
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        var taskId = request.TaskId;
        if (taskId is { } existingTaskId)
        {
            await EnsureTaskAsync(owner, existingTaskId, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.NewTaskTitle))
        {
            // Quick task from the calendar: the task is due on the block's local date; created atomically.
            var task = new TaskItem(owner, now);
            task.SetDetails(request.NewTaskTitle, null, TaskPriority.None, null, now);
            task.Schedule(TimeZones.LocalDate(request.StartsAt, context.Zone), null, null, now);
            db.Tasks.Add(task);
            taskId = task.Id;
        }

        var block = new TimeBlock(owner, now);
        block.Update(request.Title, request.Notes, request.StartsAt, request.EndsAt, taskId, now);
        var conflicts = await CheckOverlapsAsync(owner, block, context.Profile.AllowOverlappingTimeBlocks, cancellationToken);
        db.TimeBlocks.Add(block);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TimeBlockSaveResult(await GetBlockAsync(block.Id, cancellationToken), conflicts);
    }

    public async Task<TimeBlockSaveResult> UpdateAsync(Guid id, UpdateTimeBlockRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var context = await clock.GetAsync(cancellationToken);
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var block = await LoadAsync(id, cancellationToken);
        db.SetExpectedVersion(block, request.Version);
        if (request.TaskId is { } taskId)
        {
            await EnsureTaskAsync(owner, taskId, cancellationToken);
        }

        block.Update(request.Title, request.Notes, request.StartsAt, request.EndsAt, request.TaskId, clock.UtcNow);
        var conflicts = await CheckOverlapsAsync(owner, block, context.Profile.AllowOverlappingTimeBlocks, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TimeBlockSaveResult(await GetBlockAsync(id, cancellationToken), conflicts);
    }

    /// <summary>Drag-and-drop move or resize.</summary>
    public async Task<TimeBlockSaveResult> MoveAsync(Guid id, MoveTimeBlockRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var context = await clock.GetAsync(cancellationToken);
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var block = await LoadAsync(id, cancellationToken);
        db.SetExpectedVersion(block, request.Version);
        block.Move(request.StartsAt, request.EndsAt, clock.UtcNow);
        var conflicts = await CheckOverlapsAsync(owner, block, context.Profile.AllowOverlappingTimeBlocks, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TimeBlockSaveResult(await GetBlockAsync(id, cancellationToken), conflicts);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var deleted = await db.TimeBlocks.Where(b => b.Id == id && b.OwnerId == owner).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            throw new NotFoundException();
        }
    }

    public async Task<TimeBlockDto> GetBlockAsync(Guid id, CancellationToken cancellationToken) =>
        await ProjectBlocks(Owner, db.TimeBlocks.Where(b => b.Id == id)).FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException();

    /// <summary>
    /// Finds blocks overlapping <paramref name="block"/>. With overlaps disallowed, serialises the owner's planner
    /// writes (advisory lock) and rejects the save if any overlap exists.
    /// </summary>
    private async Task<IReadOnlyList<ConflictDto>> CheckOverlapsAsync(Guid owner, TimeBlock block, bool overlapsAllowed, CancellationToken cancellationToken)
    {
        if (!overlapsAllowed)
        {
            await db.LockUserScopeAsync(owner, LockScope, cancellationToken);
        }

        var conflicts = await db.TimeBlocks.AsNoTracking()
            .Where(b => b.OwnerId == owner && b.Id != block.Id && b.StartsAt < block.EndsAt && b.EndsAt > block.StartsAt)
            .OrderBy(b => b.StartsAt)
            .Take(20)
            .Select(b => new ConflictDto(b.Id, b.Title, b.StartsAt, b.EndsAt))
            .ToListAsync(cancellationToken);

        if (!overlapsAllowed && conflicts.Count > 0)
        {
            throw new ConflictException("time_block_overlap", "The time block overlaps other blocks and overlaps are disabled in settings.")
            {
                Details = conflicts,
            };
        }

        return conflicts;
    }

    private async Task EnsureTaskAsync(Guid owner, Guid taskId, CancellationToken cancellationToken)
    {
        if (!await db.Tasks.AnyAsync(t => t.Id == taskId && t.OwnerId == owner, cancellationToken))
        {
            throw new NotFoundException("Task not found.");
        }
    }

    private async Task<TimeBlock> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        return await db.TimeBlocks.FirstOrDefaultAsync(b => b.Id == id && b.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
    }

    private IQueryable<TimeBlockDto> ProjectBlocks(Guid owner, IQueryable<TimeBlock> blocks)
    {
        var tasks = db.Tasks;
        return blocks.AsNoTracking()
            .Where(b => b.OwnerId == owner)
            .OrderBy(b => b.StartsAt).ThenBy(b => b.Id)
            .Select(b => new TimeBlockDto(
                b.Id,
                b.Title,
                b.Notes,
                b.StartsAt,
                b.EndsAt,
                b.TaskId,
                tasks.Where(t => t.Id == b.TaskId && t.OwnerId == owner).Select(t => t.Title).FirstOrDefault(),
                tasks.Any(t => t.Id == b.TaskId && t.OwnerId == owner && t.Status == TaskItemStatus.Done),
                b.Version));
    }

    internal static IReadOnlyList<ConflictPairDto> ConflictPairs(IReadOnlyList<TimeBlockDto> blocks)
    {
        var pairs = new List<ConflictPairDto>();
        for (var i = 0; i < blocks.Count; i++)
        {
            for (var j = i + 1; j < blocks.Count && blocks[j].StartsAt < blocks[i].EndsAt; j++)
            {
                if (TimeBlock.Overlap(blocks[i].StartsAt, blocks[i].EndsAt, blocks[j].StartsAt, blocks[j].EndsAt))
                {
                    pairs.Add(new ConflictPairDto(blocks[i].Id, blocks[j].Id));
                }
            }
        }

        return pairs;
    }
}

public sealed record PlannerRangeQuery(DateOnly From, DateOnly To);

public sealed class PlannerRangeQueryValidator : AbstractValidator<PlannerRangeQuery>
{
    public PlannerRangeQueryValidator() => this.ValidRange(x => x.From, x => x.To, QueryLimits.MaxPlannerRangeDays);
}

public sealed class CreateTimeBlockRequestValidator : AbstractValidator<CreateTimeBlockRequest>
{
    public CreateTimeBlockRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(TimeBlock.TitleMaxLength);
        RuleFor(x => x.Notes).MaximumLength(TimeBlock.NotesMaxLength);
        RuleFor(x => x.StartsAt).ReasonableInstant();
        RuleFor(x => x.EndsAt).ReasonableInstant().GreaterThan(x => x.StartsAt);
        RuleFor(x => x.NewTaskTitle).MaximumLength(TaskItem.TitleMaxLength);
        RuleFor(x => x.NewTaskTitle).Empty().When(x => x.TaskId is not null).WithMessage("Use either an existing task or a new task title.");
    }
}

public sealed class UpdateTimeBlockRequestValidator : AbstractValidator<UpdateTimeBlockRequest>
{
    public UpdateTimeBlockRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(TimeBlock.TitleMaxLength);
        RuleFor(x => x.Notes).MaximumLength(TimeBlock.NotesMaxLength);
        RuleFor(x => x.StartsAt).ReasonableInstant();
        RuleFor(x => x.EndsAt).ReasonableInstant().GreaterThan(x => x.StartsAt);
    }
}

public sealed class MoveTimeBlockRequestValidator : AbstractValidator<MoveTimeBlockRequest>
{
    public MoveTimeBlockRequestValidator()
    {
        RuleFor(x => x.StartsAt).ReasonableInstant();
        RuleFor(x => x.EndsAt).ReasonableInstant().GreaterThan(x => x.StartsAt);
    }
}
