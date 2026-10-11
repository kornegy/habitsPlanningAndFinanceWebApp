namespace PersonalPlatform.Domain.Common;

/// <summary>
/// Marks an entity that belongs to exactly one user. The owner is assigned on creation from the
/// authenticated identity and never changes. Persistence enforces owner isolation for every
/// implementation (query filters, save-time guard, composite foreign keys).
/// </summary>
public interface IOwnedEntity
{
    Guid OwnerId { get; }
}
