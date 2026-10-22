using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Application.Organization;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Habits;
using PersonalPlatform.Domain.Habits;
using PersonalPlatform.Domain.Scheduling;
using DomainScope = PersonalPlatform.Domain.Organization.CategoryScope;

namespace PersonalPlatform.Application.Habits;

/// <summary>
/// Habit use cases. Statistics are always computed from stored completions; clients never submit counts.
/// "Today" is the owner's local date in their configured time zone.
/// </summary>
public sealed class HabitService(IAppDbContext db, ICurrentUser currentUser, UserClock clock)
{
    public const int MaxHabitsPerUser = 100;

    private Guid Owner => currentUser.UserId;

    public async Task<IReadOnlyList<HabitDto>> ListAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var today = (await clock.GetAsync(cancellationToken)).Today;
        var habits = await db.Habits.AsNoTracking()
            .Where(h => h.OwnerId == owner && (includeArchived || h.ArchivedAt == null))
            .OrderBy(h => h.ArchivedAt != null).ThenBy(h => h.CreatedAt)
            .ToListAsync(cancellationToken);
        var completions = await LoadCompletionsAsync(habits.Select(h => h.Id).ToList(), today, cancellationToken);
        return habits.Select(h => ToDto(h, completions.GetValueOrDefault(h.Id) ?? [], today)).ToList();
    }

    public async Task<HabitDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var today = (await clock.GetAsync(cancellationToken)).Today;
        var habit = await LoadAsync(id, tracked: false, cancellationToken);
        var completions = await LoadCompletionsAsync([habit.Id], today, cancellationToken);
        return ToDto(habit, completions.GetValueOrDefault(habit.Id) ?? [], today);
    }

    public async Task<HabitDto> CreateAsync(CreateHabitRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var context = await clock.GetAsync(cancellationToken);
        if (await db.Habits.CountAsync(h => h.OwnerId == owner, cancellationToken) >= MaxHabitsPerUser)
        {
            throw new ConflictException("limit_reached", $"At most {MaxHabitsPerUser} habits are allowed.");
        }

        await OrganizationService.EnsureCategoryAsync(db, owner, request.CategoryId, DomainScope.Habit, cancellationToken);
        var startDate = request.StartDate ?? context.Today;
        var habit = new Habit(owner, startDate, clock.UtcNow);
        habit.Update(request.Name, request.Description, request.Color, request.CategoryId,
            new HabitSchedule(request.ScheduleType.ToDomain(), request.Days.ToDomain(), request.IntervalDays),
            startDate, request.TargetStreak, request.ReminderTime, clock.UtcNow);
        db.Habits.Add(habit);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(habit, new HashSet<DateOnly>(), context.Today);
    }

    public async Task<HabitDto> UpdateAsync(Guid id, UpdateHabitRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var habit = await LoadAsync(id, tracked: true, cancellationToken);
        db.SetExpectedVersion(habit, request.Version);
        await OrganizationService.EnsureCategoryAsync(db, owner, request.CategoryId, DomainScope.Habit, cancellationToken);
        habit.Update(request.Name, request.Description, request.Color, request.CategoryId,
            new HabitSchedule(request.ScheduleType.ToDomain(), request.Days.ToDomain(), request.IntervalDays),
            request.StartDate, request.TargetStreak, request.ReminderTime, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<HabitDto> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken)
    {
        var habit = await LoadAsync(id, tracked: true, cancellationToken);
        if (archived)
        {
            habit.Archive(clock.UtcNow);
        }
        else
        {
            habit.Restore(clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes the habit and its whole history.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var deleted = await db.Habits.Where(h => h.Id == id && h.OwnerId == owner).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            throw new NotFoundException();
        }
    }

    /// <summary>
    /// Starts over: removes the check-offs of one habit (or of all active habits when <paramref name="id"/> is null)
    /// and moves their start date to today, so streaks and completion rates are counted afresh. Archived habits
    /// keep their history in a global reset.
    /// </summary>
    public async Task<HabitResetResult> ResetAsync(Guid? id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var today = (await clock.GetAsync(cancellationToken)).Today;
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        List<Habit> habits = id is { } habitId
            ? [await LoadAsync(habitId, tracked: true, cancellationToken)]
            : await db.Habits.Where(h => h.OwnerId == owner && h.ArchivedAt == null).ToListAsync(cancellationToken);
        var ids = habits.Select(h => h.Id).ToList();
        var removed = await db.HabitCompletions.Where(c => c.OwnerId == owner && ids.Contains(c.HabitId)).ExecuteDeleteAsync(cancellationToken);
        foreach (var habit in habits)
        {
            habit.Restart(today, clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HabitResetResult(habits.Count, removed);
    }

    /// <summary>
    /// Marks the habit done for a local date. Idempotent: checking off an already checked date succeeds without
    /// creating a second record (also under concurrent requests, thanks to the unique index).
    /// </summary>
    public async Task<HabitCompletionResult> CompleteAsync(Guid id, DateOnly date, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var today = (await clock.GetAsync(cancellationToken)).Today;
        var habit = await LoadAsync(id, tracked: false, cancellationToken);
        var exists = await db.HabitCompletions.AnyAsync(c => c.HabitId == id && c.OwnerId == owner && c.Date == date, cancellationToken);
        if (!exists)
        {
            db.HabitCompletions.Add(habit.Complete(date, today, clock.UtcNow));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (UniqueConstraintException)
            {
                db.ResetTracking(); // concurrent duplicate: the date is checked off exactly once
            }
        }

        return await CompletionResultAsync(habit, date, today, cancellationToken);
    }

    public async Task<HabitCompletionResult> UncompleteAsync(Guid id, DateOnly date, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var today = (await clock.GetAsync(cancellationToken)).Today;
        var habit = await LoadAsync(id, tracked: false, cancellationToken);
        await db.HabitCompletions.Where(c => c.HabitId == id && c.OwnerId == owner && c.Date == date).ExecuteDeleteAsync(cancellationToken);
        return await CompletionResultAsync(habit, date, today, cancellationToken);
    }

    public async Task<HabitHistoryDto> HistoryAsync(Guid id, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var habit = await LoadAsync(id, tracked: false, cancellationToken);
        var completed = await db.HabitCompletions.AsNoTracking()
            .Where(c => c.HabitId == id && c.OwnerId == owner && c.Date >= from && c.Date <= to)
            .OrderBy(c => c.Date)
            .Select(c => c.Date)
            .ToListAsync(cancellationToken);
        var scheduled = new List<DateOnly>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (habit.Schedule.IsDue(date, habit.StartDate))
            {
                scheduled.Add(date);
            }
        }

        return new HabitHistoryDto(id, from, to, scheduled, completed);
    }

    public async Task<HabitStatsDto> StatsAsync(Guid id, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var context = await clock.GetAsync(cancellationToken);
        var habit = await LoadAsync(id, tracked: false, cancellationToken);
        var completions = (await LoadCompletionsAsync([id], context.Today, cancellationToken)).GetValueOrDefault(id) ?? [];
        var overall = HabitStatistics.Calculate(habit.Schedule, habit.StartDate, completions, context.Today, from, to);

        var weeks = new List<PeriodStatDto>();
        for (var start = CalendarMath.StartOfWeek(from, context.Profile.FirstDayOfWeek); start <= to; start = start.AddDays(7))
        {
            weeks.Add(Period(habit, completions, context.Today, start, start.AddDays(6)));
        }

        var months = new List<PeriodStatDto>();
        for (var start = CalendarMath.StartOfMonth(from); start <= to; start = start.AddMonths(1))
        {
            months.Add(Period(habit, completions, context.Today, start, CalendarMath.EndOfMonth(start)));
        }

        return new HabitStatsDto(id, from, to, overall.CurrentStreak, overall.LongestStreak, overall.ScheduledCount, overall.CompletedCount, overall.CompletionRate, weeks, months);
    }

    private static PeriodStatDto Period(Habit habit, IReadOnlySet<DateOnly> completions, DateOnly today, DateOnly start, DateOnly end)
    {
        var (scheduled, completed) = HabitStatistics.CountPeriod(habit.Schedule, habit.StartDate, completions, today, start, end);
        var rate = scheduled == 0 ? 0m : decimal.Round(completed * 100m / scheduled, 1, MidpointRounding.AwayFromZero);
        return new PeriodStatDto(start, end, scheduled, completed, rate);
    }

    private async Task<HabitCompletionResult> CompletionResultAsync(Habit habit, DateOnly date, DateOnly today, CancellationToken cancellationToken)
    {
        var completions = (await LoadCompletionsAsync([habit.Id], today, cancellationToken)).GetValueOrDefault(habit.Id) ?? [];
        var historyStart = today.AddDays(-HabitStatistics.MaxHistoryDays);
        var streak = HabitStatistics.CurrentStreak(habit.Schedule, habit.StartDate, historyStart > habit.StartDate ? historyStart : habit.StartDate, completions, today);
        return new HabitCompletionResult(habit.Id, date, completions.Contains(date), streak);
    }

    private async Task<Dictionary<Guid, HashSet<DateOnly>>> LoadCompletionsAsync(IReadOnlyCollection<Guid> habitIds, DateOnly today, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var since = today.AddDays(-HabitStatistics.MaxHistoryDays);
        var rows = await db.HabitCompletions.AsNoTracking()
            .Where(c => c.OwnerId == owner && habitIds.Contains(c.HabitId) && c.Date >= since)
            .Select(c => new { c.HabitId, c.Date })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.HabitId).ToDictionary(g => g.Key, g => g.Select(r => r.Date).ToHashSet());
    }

    private async Task<Habit> LoadAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var query = tracked ? db.Habits : db.Habits.AsNoTracking();
        return await query.FirstOrDefaultAsync(h => h.Id == id && h.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
    }

    internal static HabitDto ToDto(Habit h, IReadOnlySet<DateOnly> completions, DateOnly today)
    {
        var historyStart = today.AddDays(-HabitStatistics.MaxHistoryDays);
        var from = historyStart > h.StartDate ? historyStart : h.StartDate;
        var current = HabitStatistics.CurrentStreak(h.Schedule, h.StartDate, from, completions, today);
        var longest = Math.Max(current, HabitStatistics.LongestStreak(h.Schedule, h.StartDate, from, completions, today));
        return new HabitDto(
            h.Id, h.Name, h.Description, h.Color, h.CategoryId, h.ScheduleType.ToContract(), h.Days.ToContract(), h.IntervalDays,
            h.StartDate, h.TargetStreak, h.ReminderTime, h.IsArchived, h.Schedule.IsDue(today, h.StartDate), completions.Contains(today),
            current, longest, h.Version);
    }
}

public sealed class CreateHabitRequestValidator : AbstractValidator<CreateHabitRequest>
{
    public CreateHabitRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Habit.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Habit.DescriptionMaxLength);
        RuleFor(x => x.Color).Must(c => c is null || Domain.Common.ColorHex.IsValid(c)).WithMessage("Color must be in #RRGGBB format.");
        RuleFor(x => x.ScheduleType).IsInEnum();
        RuleFor(x => x.IntervalDays).InclusiveBetween(1, HabitSchedule.MaxIntervalDays);
        RuleFor(x => x.StartDate).ReasonableDate();
        RuleFor(x => x.TargetStreak).InclusiveBetween(1, Habit.MaxTargetStreak);
    }
}

public sealed class UpdateHabitRequestValidator : AbstractValidator<UpdateHabitRequest>
{
    public UpdateHabitRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Habit.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Habit.DescriptionMaxLength);
        RuleFor(x => x.Color).Must(c => c is null || Domain.Common.ColorHex.IsValid(c)).WithMessage("Color must be in #RRGGBB format.");
        RuleFor(x => x.ScheduleType).IsInEnum();
        RuleFor(x => x.IntervalDays).InclusiveBetween(1, HabitSchedule.MaxIntervalDays);
        RuleFor(x => x.StartDate).ReasonableDate();
        RuleFor(x => x.TargetStreak).InclusiveBetween(1, Habit.MaxTargetStreak);
    }
}

/// <summary>Date range used by habit history and statistics endpoints.</summary>
public sealed record HabitRangeQuery(DateOnly From, DateOnly To);

public sealed class HabitRangeQueryValidator : AbstractValidator<HabitRangeQuery>
{
    public HabitRangeQueryValidator() => this.ValidRange(x => x.From, x => x.To, QueryLimits.MaxRangeDays);
}
