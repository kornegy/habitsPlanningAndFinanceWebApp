using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Finance;
using PersonalPlatform.Domain.Habits;
using PersonalPlatform.Domain.Notes;
using PersonalPlatform.Domain.Organization;
using PersonalPlatform.Domain.Planner;
using PersonalPlatform.Domain.Tasks;
using PersonalPlatform.Domain.Users;
using PersonalPlatform.Infrastructure.Identity;

namespace PersonalPlatform.Infrastructure.Persistence;

/// <summary>
/// EF Core context for all application data.
/// </summary>
/// <remarks>
/// Owner isolation is enforced here in two independent ways, in addition to the explicit owner filters in
/// every application query and the composite <c>(id, owner_id)</c> foreign keys in the schema:
/// <list type="number">
/// <item>A global query filter limits every <see cref="IOwnedEntity"/> set to <see cref="EffectiveOwnerId"/>.
/// Without an authenticated user the effective owner is <see cref="Guid.Empty"/>, so queries return nothing
/// (fail closed).</item>
/// <item><see cref="SaveChangesAsync(CancellationToken)"/> refuses to add, modify or delete an owned entity
/// whose owner differs from the effective owner.</item>
/// </list>
/// </remarks>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IAppDbContext, IDataProtectionKeyContext
{
    public const string Schema = "app";

    private Guid? _impersonatedOwnerId;

    /// <summary>The only owner whose rows this context may read or write.</summary>
    public Guid EffectiveOwnerId =>
        _impersonatedOwnerId ?? (currentUser.IsAuthenticated ? currentUser.UserId : Guid.Empty);

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<Subtask> Subtasks => Set<Subtask>();

    public DbSet<TaskTag> TaskTags => Set<TaskTag>();

    public DbSet<TaskSeries> TaskSeries => Set<TaskSeries>();

    public DbSet<Habit> Habits => Set<Habit>();

    public DbSet<HabitCompletion> HabitCompletions => Set<HabitCompletion>();

    public DbSet<TimeBlock> TimeBlocks => Set<TimeBlock>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<FinanceCategory> FinanceCategories => Set<FinanceCategory>();

    public DbSet<FinancialTransaction> Transactions => Set<FinancialTransaction>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<Budget> Budgets => Set<Budget>();

    public DbSet<RecurringTransaction> RecurringTransactions => Set<RecurringTransaction>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<NoteTag> NoteTags => Set<NoteTag>();

    public DbSet<NoteRevision> NoteRevisions => Set<NoteRevision>();

    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>
    /// Lets infrastructure code act for a specific user when no access token exists yet (registration
    /// creates the new user's profile). Not exposed to the application layer.
    /// </summary>
    internal IDisposable ActAsOwner(Guid ownerId)
    {
        var previous = _impersonatedOwnerId;
        _impersonatedOwnerId = ownerId;
        return new Restore(() => _impersonatedOwnerId = previous);
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        await Database.BeginTransactionAsync(cancellationToken);

    public void SetExpectedVersion<TEntity>(TEntity entity, uint expectedVersion)
        where TEntity : class =>
        Entry(entity).Property("Version").OriginalValue = expectedVersion;

    public async Task LockUserScopeAsync(Guid userId, string scope, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Advisory locks require an open transaction.");
        }

        var key = AdvisoryLockKey(userId, scope);
        await Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
    }

    public void ResetTracking() => ChangeTracker.Clear();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceOwnership();
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConflictException.StaleVersion();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw UniqueConstraintException.ForConstraint(pg.ConstraintName, ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw new ConflictException("reference_conflict", "The operation references an item that does not exist or is still in use.");
        }
    }

    /// <summary>
    /// Synchronous save, used only by the Data Protection key repository (which offers no async API).
    /// Applies the same ownership guard. Application code always uses <see cref="SaveChangesAsync(CancellationToken)"/>.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        IdentityModel.Configure(builder);
        ApplyOwnerFilters(builder);
    }

    internal static long AdvisoryLockKey(Guid userId, string scope)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{scope}"), hash);
        return BitConverter.ToInt64(hash[..8]);
    }

    private void ApplyOwnerFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes().Where(t => typeof(IOwnedEntity).IsAssignableFrom(t.ClrType)))
        {
            // e => e.OwnerId == this.EffectiveOwnerId (EF re-evaluates the context member per query)
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(
                Expression.Property(parameter, nameof(IOwnedEntity.OwnerId)),
                Expression.Property(Expression.Constant(this), nameof(EffectiveOwnerId)));
            builder.Entity(entityType.ClrType).HasQueryFilter("owner", Expression.Lambda(body, parameter));
        }
    }

    private void EnforceOwnership()
    {
        var owner = EffectiveOwnerId;
        foreach (var entry in ChangeTracker.Entries<IOwnedEntity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && (owner == Guid.Empty || entry.Entity.OwnerId != owner))
            {
                // Indicates a programming error, never a user error; refuse rather than leak or corrupt data.
                throw new InvalidOperationException($"Refusing to save {entry.Metadata.ClrType.Name} owned by another user.");
            }
        }
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
