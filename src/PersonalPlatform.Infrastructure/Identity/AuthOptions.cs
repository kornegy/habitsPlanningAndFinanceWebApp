using System.ComponentModel.DataAnnotations;

namespace PersonalPlatform.Infrastructure.Identity;

/// <summary>One symmetric signing key. Several keys may be configured to rotate without downtime.</summary>
public sealed class JwtSigningKeyOptions
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Base64-encoded key material, at least 256 bits. Supplied via secrets, never committed.</summary>
    [Required]
    public string Key { get; set; } = string.Empty;
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "personal-platform";

    [Required]
    public string Audience { get; set; } = "personal-platform-api";

    /// <summary>Access token lifetime. Short by design: revocation is also checked per request.</summary>
    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 5;

    /// <summary>Key id used to sign new tokens. Other configured keys are accepted for validation only.</summary>
    public string ActiveKeyId { get; set; } = string.Empty;

    public List<JwtSigningKeyOptions> SigningKeys { get; set; } = [];
}

public sealed class SessionOptions
{
    public const string SectionName = "Sessions";

    /// <summary>Lifetime of each individual refresh token (sliding window between refreshes).</summary>
    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>Absolute session lifetime; the user must sign in again afterwards.</summary>
    [Range(1, 365)]
    public int SessionMaxDays { get; set; } = 30;

    /// <summary>Signing in beyond this many active sessions revokes the least recently used one.</summary>
    [Range(1, 100)]
    public int MaxActiveSessionsPerUser { get; set; } = 10;

    /// <summary>Revoked/expired sessions are kept this long for the user's security review, then deleted.</summary>
    [Range(1, 365)]
    public int RetentionDays { get; set; } = 30;
}

public sealed class AccountEmailOptions
{
    public const string SectionName = "AccountEmails";

    /// <summary>Public base URL of the web app used to build links in emails (e.g. https://app.example.com).</summary>
    [Required]
    [Url]
    public string PublicBaseUrl { get; set; } = "https://localhost:5001";

    /// <summary>Minimum interval between account emails (confirmation, reset) to the same address.</summary>
    [Range(0, 3600)]
    public int MinSecondsBetweenEmails { get; set; } = 60;
}
