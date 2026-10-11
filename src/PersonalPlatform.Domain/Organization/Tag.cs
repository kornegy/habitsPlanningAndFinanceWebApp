using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Domain.Organization;

/// <summary>User-defined label shared by tasks and notes. Names are unique per user (case-insensitive).</summary>
public sealed class Tag : IOwnedEntity
{
    public const int NameMaxLength = 40;

    private Tag()
    {
    }

    public Tag(Guid ownerId, string name, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        CreatedAt = now;
        Rename(name);
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Lower-cased name used for the per-user uniqueness constraint.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public void Rename(string name)
    {
        Name = Rule.RequireText(name, NameMaxLength, "invalid_tag", "Tag");
        NormalizedName = Normalize(Name);
    }

    public static string Normalize(string name) => name.Trim().ToLowerInvariant();
}
