using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts.Habits;
using PersonalPlatform.Domain.Habits;

namespace PersonalPlatform.Application.Habits;

/// <summary>
/// Use cases for habits the user is quitting. Progress is derived from the quit date and stored relapses only;
/// "today" is the owner's local date in their configured time zone.
/// </summary>
public sealed class QuitHabitService(IAppDbContext db, ICurrentUser currentUser, UserClock clock)
{
    public const int MaxQuitHabitsPerUser = 50;
    public const int MaxRelapsesInDetail = 500;

    private Guid Owner => currentUser.UserId;

    public async Task<IReadOnlyList<QuitHabitDto>> ListAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var today = (await clock.GetAsync(cancellationToken)).Today;
        var habits = await db.QuitHabits.AsNoTracking()
            .Where(h => h.OwnerId == owner && (includeArchived || h.ArchivedAt == null))
            .OrderBy(h => h.ArchivedAt != null).ThenBy(h => h.CreatedAt)
            .ToListAsync(cancellationToken);
        var ids = habits.Select(h => h.Id).ToList();
        var relapses = (await db.QuitHabitRelapses.AsNoTracking()
                .Where(r => r.OwnerId == owner && ids.Contains(r.QuitHabitId))
                .Select(r => new { r.QuitHabitId, r.Date })
                .ToListAsync(cancellationToken))
            .ToLookup(r => r.QuitHabitId, r => r.Date);
        return habits.Select(h => ToDto(h, relapses[h.Id], today)).ToList();
    }

    public async Task<QuitHabitDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var today = (await clock.GetAsync(cancellationToken)).Today;
        var habit = await LoadAsync(id, tracked: false, cancellationToken);
        var relapses = await db.QuitHabitRelapses.AsNoTracking()
            .Where(r => r.OwnerId == owner && r.QuitHabitId == id)
            .OrderByDescending(r => r.Date)
            .Select(r => new QuitHabitRelapseDto(r.Date, r.Note))
            .ToListAsync(cancellationToken);
        return new QuitHabitDetailDto(ToDto(habit, relapses.Select(r => r.Date), today), relapses.Take(MaxRelapsesInDetail).ToList());
    }

    public async Task<QuitHabitDetailDto> CreateAsync(CreateQuitHabitRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var today = (await clock.GetAsync(cancellationToken)).Today;
        if (await db.QuitHabits.CountAsync(h => h.OwnerId == owner, cancellationToken) >= MaxQuitHabitsPerUser)
        {
            throw new ConflictException("limit_reached", $"At most {MaxQuitHabitsPerUser} habits to quit are allowed.");
        }

        var habit = new QuitHabit(owner, clock.UtcNow);
        habit.Update(request.Name, request.Description, request.Color, request.QuitDate ?? today, today, clock.UtcNow);
        db.QuitHabits.Add(habit);
        await db.SaveChangesAsync(cancellationToken);
        return new QuitHabitDetailDto(ToDto(habit, [], today), []);
    }

    public async Task<QuitHabitDetailDto> UpdateAsync(Guid id, UpdateQuitHabitRequest request, CancellationToken cancellationToken)
    {
        var today = (await clock.GetAsync(cancellationToken)).Today;
        var habit = await LoadAsync(id, tracked: true, cancellationToken);
        db.SetExpectedVersion(habit, request.Version);
        habit.Update(request.Name, request.Description, request.Color, request.QuitDate, today, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<QuitHabitDetailDto> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken)
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

    /// <summary>Deletes the habit together with its relapse history.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var deleted = await db.QuitHabits.Where(h => h.Id == id && h.OwnerId == owner).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            throw new NotFoundException();
        }
    }

    /// <summary>
    /// Records a relapse on a local date. Idempotent per date: repeating it keeps a single record (also under
    /// concurrent requests, thanks to the unique index) and updates nothing.
    /// </summary>
    public async Task<QuitHabitDetailDto> LogRelapseAsync(Guid id, LogRelapseRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var today = (await clock.GetAsync(cancellationToken)).Today;
        var habit = await LoadAsync(id, tracked: false, cancellationToken);
        var relapse = habit.Relapse(request.Date, request.Note, today, clock.UtcNow);
        if (!await db.QuitHabitRelapses.AnyAsync(r => r.OwnerId == owner && r.QuitHabitId == id && r.Date == request.Date, cancellationToken))
        {
            db.QuitHabitRelapses.Add(relapse);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (UniqueConstraintException)
            {
                db.ResetTracking(); // concurrent duplicate: the day is recorded exactly once
            }
        }

        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Removes the relapse recorded on a date (idempotent), e.g. one logged by mistake.</summary>
    public async Task<QuitHabitDetailDto> DeleteRelapseAsync(Guid id, DateOnly date, CancellationToken cancellationToken)
    {
        var owner = Owner;
        await LoadAsync(id, tracked: false, cancellationToken);
        await db.QuitHabitRelapses.Where(r => r.OwnerId == owner && r.QuitHabitId == id && r.Date == date).ExecuteDeleteAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<QuitHabit> LoadAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var query = tracked ? db.QuitHabits : db.QuitHabits.AsNoTracking();
        return await query.FirstOrDefaultAsync(h => h.Id == id && h.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
    }

    internal static QuitHabitDto ToDto(QuitHabit h, IEnumerable<DateOnly> relapses, DateOnly today)
    {
        var progress = QuitHabitStatistics.Calculate(h.QuitDate, relapses, today);
        return new QuitHabitDto(
            h.Id, h.Name, h.Description, h.Color, h.QuitDate, h.IsArchived, progress.CurrentStreakDays, progress.LongestStreakDays,
            progress.RelapseCount, progress.LastRelapse, progress.NextMilestoneDays, h.Version);
    }
}

public sealed class CreateQuitHabitRequestValidator : AbstractValidator<CreateQuitHabitRequest>
{
    public CreateQuitHabitRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(QuitHabit.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(QuitHabit.DescriptionMaxLength);
        RuleFor(x => x.Color).Must(c => c is null || Domain.Common.ColorHex.IsValid(c)).WithMessage("Color must be in #RRGGBB format.");
        RuleFor(x => x.QuitDate).ReasonableDate();
    }
}

public sealed class UpdateQuitHabitRequestValidator : AbstractValidator<UpdateQuitHabitRequest>
{
    public UpdateQuitHabitRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(QuitHabit.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(QuitHabit.DescriptionMaxLength);
        RuleFor(x => x.Color).Must(c => c is null || Domain.Common.ColorHex.IsValid(c)).WithMessage("Color must be in #RRGGBB format.");
        RuleFor(x => x.QuitDate).ReasonableDate();
    }
}

public sealed class LogRelapseRequestValidator : AbstractValidator<LogRelapseRequest>
{
    public LogRelapseRequestValidator()
    {
        RuleFor(x => x.Date).ReasonableDate();
        RuleFor(x => x.Note).MaximumLength(QuitHabitRelapse.NoteMaxLength);
    }
}
