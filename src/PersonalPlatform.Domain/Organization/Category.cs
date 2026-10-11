using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Domain.Organization;

public enum CategoryScope
{
    Task = 0,
    Habit = 1,
}

/// <summary>Grouping for tasks or habits (finance has its own <c>FinanceCategory</c>).</summary>
public sealed class Category : IOwnedEntity
{
    public const int NameMaxLength = 60;

    private Category()
    {
    }

    public Category(Guid ownerId, CategoryScope scope, string name, string? color, DateTimeOffset now)
    {
        Rule.Require(Enum.IsDefined(scope), "invalid_scope", "Unknown category scope.");
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Scope = scope;
        CreatedAt = now;
        Update(name, color);
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public CategoryScope Scope { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Color { get; private set; } = ColorHex.Default;

    public DateTimeOffset CreatedAt { get; private set; }

    public uint Version { get; private set; }

    public void Update(string name, string? color)
    {
        Name = Rule.RequireText(name, NameMaxLength, "invalid_name", "Name");
        Color = ColorHex.Normalize(color);
    }
}
