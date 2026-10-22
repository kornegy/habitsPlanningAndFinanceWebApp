using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PersonalPlatform.Domain.Finance;
using PersonalPlatform.Domain.Habits;
using PersonalPlatform.Domain.Notes;
using PersonalPlatform.Domain.Organization;
using PersonalPlatform.Domain.Planner;
using PersonalPlatform.Domain.Tasks;
using PersonalPlatform.Domain.Users;

namespace PersonalPlatform.Application.Common;

/// <summary>
/// Unit of work over the application's data. Every owned set is additionally filtered by the current
/// user at the persistence layer; services still filter by owner explicitly (defence in depth).
/// </summary>
public interface IAppDbContext
{
    DbSet<UserProfile> UserProfiles { get; }

    DbSet<Category> Categories { get; }

    DbSet<Tag> Tags { get; }

    DbSet<TaskItem> Tasks { get; }

    DbSet<Subtask> Subtasks { get; }

    DbSet<TaskTag> TaskTags { get; }

    DbSet<TaskSeries> TaskSeries { get; }

    DbSet<Habit> Habits { get; }

    DbSet<HabitCompletion> HabitCompletions { get; }

    DbSet<QuitHabit> QuitHabits { get; }

    DbSet<QuitHabitRelapse> QuitHabitRelapses { get; }

    DbSet<TimeBlock> TimeBlocks { get; }

    DbSet<Account> Accounts { get; }

    DbSet<FinanceCategory> FinanceCategories { get; }

    DbSet<FinancialTransaction> Transactions { get; }

    DbSet<LedgerEntry> LedgerEntries { get; }

    DbSet<Budget> Budgets { get; }

    DbSet<RecurringTransaction> RecurringTransactions { get; }

    DbSet<Note> Notes { get; }

    DbSet<NoteTag> NoteTags { get; }

    DbSet<NoteRevision> NoteRevisions { get; }

    DbSet<JournalEntry> JournalEntries { get; }

    /// <summary>
    /// Saves pending changes. Translates unique violations to <see cref="UniqueConstraintException"/> and
    /// stale optimistic-concurrency versions to <see cref="ConflictException"/>.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the expected concurrency version of a tracked entity to the value the client last read, so the
    /// following save fails with a conflict if somebody else changed the row in the meantime.
    /// </summary>
    void SetExpectedVersion<TEntity>(TEntity entity, uint expectedVersion)
        where TEntity : class;

    /// <summary>
    /// Serialises concurrent operations of one user on one resource kind inside the current transaction
    /// (PostgreSQL transaction-scoped advisory lock). Requires an open transaction.
    /// </summary>
    Task LockUserScopeAsync(Guid userId, string scope, CancellationToken cancellationToken);

    /// <summary>Clears the change tracker, e.g. after a failed save that will be retried or reported.</summary>
    void ResetTracking();
}
