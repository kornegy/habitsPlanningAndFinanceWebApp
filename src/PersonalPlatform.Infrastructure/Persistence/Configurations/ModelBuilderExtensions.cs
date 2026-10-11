using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalPlatform.Domain.Common;
using PersonalPlatform.Infrastructure.Identity;

namespace PersonalPlatform.Infrastructure.Persistence.Configurations;

internal static class ModelBuilderExtensions
{
    public const string MoneyType = "numeric(19,4)";

    /// <summary>Owner foreign key to <c>users</c>; deleting a user deletes all of their data.</summary>
    public static void HasOwner<T>(this EntityTypeBuilder<T> builder)
        where T : class, IOwnedEntity =>
        builder.HasOne<AppUser>().WithMany().HasForeignKey(e => e.OwnerId).OnDelete(DeleteBehavior.Cascade);

    /// <summary>Makes <c>(id, owner_id)</c> referenceable by composite foreign keys of other owned tables.</summary>
    public static void HasOwnerScopedKey<T>(this EntityTypeBuilder<T> builder)
        where T : class, IOwnedEntity =>
        builder.HasAlternateKey("Id", nameof(IOwnedEntity.OwnerId));

    /// <summary>
    /// Composite foreign key <c>(fkColumn, owner_id) → principal(id, owner_id)</c>. The database itself then
    /// rejects any reference to an object that belongs to another user, even if application checks were
    /// bypassed. <see cref="DeleteBehavior.NoAction"/> is checked at statement end, so cascading user deletion
    /// still works; application code clears such references explicitly before deleting the principal.
    /// </summary>
    public static ReferenceCollectionBuilder<TPrincipal, TDependent> ReferencesOwned<TDependent, TPrincipal>(
        this EntityTypeBuilder<TDependent> builder,
        string foreignKeyProperty,
        DeleteBehavior onDelete = DeleteBehavior.NoAction)
        where TDependent : class, IOwnedEntity
        where TPrincipal : class, IOwnedEntity =>
        builder.HasOne<TPrincipal>()
            .WithMany()
            .HasForeignKey(foreignKeyProperty, nameof(IOwnedEntity.OwnerId))
            .HasPrincipalKey("Id", nameof(IOwnedEntity.OwnerId))
            .OnDelete(onDelete);

    /// <summary>Maps the <c>Version</c> property to PostgreSQL's <c>xmin</c> system column (optimistic concurrency).</summary>
    public static void HasXminVersion<T>(this EntityTypeBuilder<T> builder)
        where T : class =>
        builder.Property<uint>("Version").IsRowVersion();
}
