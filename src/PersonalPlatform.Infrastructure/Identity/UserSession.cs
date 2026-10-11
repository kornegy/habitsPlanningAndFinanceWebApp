namespace PersonalPlatform.Infrastructure.Identity;

public enum SessionRevocationReason
{
    Logout = 0,
    RevokedByUser = 1,
    PasswordChanged = 2,
    RefreshTokenReuse = 3,
    SessionLimit = 4,
    PasswordReset = 5,
}

/// <summary>
/// A signed-in device/browser. Equals one refresh-token family: every rotated token of the family belongs to
/// the same session, and revoking the session stops both refreshing and (via the <c>sid</c> claim check)
/// any still-valid access token immediately.
/// </summary>
public sealed class UserSession
{
    public const int UserAgentMaxLength = 256;
    public const int IpAddressMaxLength = 45;

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastUsedAt { get; set; }

    /// <summary>Absolute lifetime: refreshing never extends a session beyond this instant.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public SessionRevocationReason? RevocationReason { get; set; }

    public string? UserAgent { get; set; }

    public string? IpAddress { get; set; }

    public List<RefreshToken> RefreshTokens { get; } = [];

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(SessionRevocationReason reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevocationReason = reason;
    }
}

/// <summary>
/// One refresh token of a session. Only the SHA-256 hash of the 256-bit random token is stored. A token is
/// single-use: it is marked consumed when exchanged and replaced by a new one.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public Guid? ReplacedById { get; set; }
}
