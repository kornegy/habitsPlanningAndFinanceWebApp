using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalPlatform.Domain.Finance;
using PersonalPlatform.Domain.Organization;
using PersonalPlatform.Domain.Users;

namespace PersonalPlatform.Infrastructure.Persistence.Configurations;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> b)
    {
        b.ToTable("user_profiles", t => t.HasCheckConstraint("ck_user_profiles_theme", "theme BETWEEN 0 AND 2"));
        b.HasKey(p => p.OwnerId);
        b.HasOwner();
        b.Property(p => p.DisplayName).HasMaxLength(UserProfile.DisplayNameMaxLength);
        b.Property(p => p.TimeZoneId).HasMaxLength(TimeZones.MaxIdLength);
        b.Property(p => p.Locale).HasMaxLength(10);
        b.Property(p => p.CurrencyCode).HasMaxLength(Currencies.CodeLength).IsFixedLength();
        b.HasXminVersion();
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories");
        b.HasKey(c => c.Id);
        b.HasOwner();
        b.HasOwnerScopedKey();
        b.Property(c => c.Name).HasMaxLength(Category.NameMaxLength);
        b.Property(c => c.Color).HasMaxLength(7).IsFixedLength();
        b.HasIndex(c => new { c.OwnerId, c.Scope, c.Name }).IsUnique();
        b.HasXminVersion();
    }
}

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable("tags");
        b.HasKey(t => t.Id);
        b.HasOwner();
        b.HasOwnerScopedKey();
        b.Property(t => t.Name).HasMaxLength(Tag.NameMaxLength);
        b.Property(t => t.NormalizedName).HasMaxLength(Tag.NameMaxLength);
        b.HasIndex(t => new { t.OwnerId, t.NormalizedName }).IsUnique();
    }
}
