using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Contracts.Admin;
using PersonalPlatform.Infrastructure.Identity;

namespace PersonalPlatform.Infrastructure.Persistence;

/// <summary>Identity, session and refresh-token tables.</summary>
internal static class IdentityModel
{
    /// <summary>Fixed id of the seeded administrator role (seeded by migration, never by the app at runtime).</summary>
    public static readonly Guid AdminRoleId = new("0195f3a0-0000-7000-8000-000000000001");

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<AppUser>(b =>
        {
            b.ToTable("users");
            b.Property(u => u.CreatedAt).IsRequired();
        });
        builder.Entity<AppRole>(b =>
        {
            b.ToTable("roles");
            b.HasData(new AppRole
            {
                Id = AdminRoleId,
                Name = Roles.Admin,
                NormalizedName = Roles.Admin.ToUpperInvariant(),
                ConcurrencyStamp = "4c1d7b4e-6f0e-4b8a-9a36-3d4a7f9a2c11",
            });
        });
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        builder.Entity<UserSession>(b =>
        {
            b.ToTable("user_sessions");
            b.HasKey(s => s.Id);
            b.Property(s => s.UserAgent).HasMaxLength(UserSession.UserAgentMaxLength);
            b.Property(s => s.IpAddress).HasMaxLength(UserSession.IpAddressMaxLength);
            b.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(s => s.RefreshTokens).WithOne().HasForeignKey(t => t.SessionId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(s => new { s.UserId, s.RevokedAt, s.ExpiresAt });
            b.HasIndex(s => s.ExpiresAt);
        });

        builder.Entity<RefreshToken>(b =>
        {
            b.ToTable("refresh_tokens");
            b.HasKey(t => t.Id);
            b.Property(t => t.TokenHash).IsRequired().HasMaxLength(32);
            b.HasIndex(t => t.TokenHash).IsUnique();
            b.HasIndex(t => t.ExpiresAt);
        });

        builder.Entity<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>().ToTable("data_protection_keys");
    }
}
