using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Domain.Finance;

public enum CategoryKind
{
    Expense = 0,
    Income = 1,
}

/// <summary>Income or expense category. At most two levels: a category and its subcategories.</summary>
public sealed class FinanceCategory : IOwnedEntity
{
    public const int NameMaxLength = 60;

    private FinanceCategory()
    {
    }

    public FinanceCategory(Guid ownerId, string name, CategoryKind kind, FinanceCategory? parent, DateTimeOffset now)
    {
        Rule.Require(Enum.IsDefined(kind), "invalid_category_kind", "Unknown category kind.");
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Kind = kind;
        CreatedAt = now;
        SetParent(parent);
        Rename(name, now);
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public CategoryKind Kind { get; private set; }

    public Guid? ParentId { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = Rule.RequireText(name, NameMaxLength, "invalid_name", "Name");
        UpdatedAt = now;
    }

    public void SetParent(FinanceCategory? parent)
    {
        if (parent is null)
        {
            ParentId = null;
            return;
        }

        Rule.Require(parent.OwnerId == OwnerId, "invalid_parent", "Parent category not found.");
        Rule.Require(parent.Id != Id, "invalid_parent", "A category cannot be its own parent.");
        Rule.Require(parent.ParentId is null, "invalid_parent", "Only two category levels are supported.");
        Rule.Require(parent.Kind == Kind, "invalid_parent", "Parent category must be of the same kind.");
        ParentId = parent.Id;
    }

    public void Archive(DateTimeOffset now)
    {
        ArchivedAt ??= now;
        UpdatedAt = now;
    }

    public void Restore(DateTimeOffset now)
    {
        ArchivedAt = null;
        UpdatedAt = now;
    }
}
