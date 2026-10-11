using Microsoft.AspNetCore.Identity;

namespace PersonalPlatform.Infrastructure.Identity;

/// <summary>ASP.NET Core Identity user. Passwords are stored only as Identity password hashes (PBKDF2).</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last time a confirmation or "account exists" email was sent; throttles email abuse.</summary>
    public DateTimeOffset? LastAccountEmailSentAt { get; set; }

    /// <summary>Last time a password reset email was sent; throttles email abuse.</summary>
    public DateTimeOffset? LastPasswordResetSentAt { get; set; }
}

public sealed class AppRole : IdentityRole<Guid>
{
}
