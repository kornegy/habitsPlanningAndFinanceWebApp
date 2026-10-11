namespace PersonalPlatform.Contracts.Auth;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName, string? TimeZoneId, string? Locale);

public sealed record LoginRequest(string Email, string Password);

public sealed record ConfirmEmailRequest(Guid UserId, string Token);

public sealed record EmailRequest(string Email);

public sealed record ResetPasswordRequest(Guid UserId, string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Account deletion requires the password and the literal confirmation word.</summary>
public sealed record DeleteAccountRequest(string Password, string Confirmation)
{
    public const string RequiredConfirmation = "DELETE";
}

/// <summary>
/// Returned by login and refresh. The access token must be kept in memory only. The refresh token is never
/// part of a response body: it travels exclusively in an HttpOnly cookie.
/// </summary>
public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt, CurrentUserDto User);

public sealed record CurrentUserDto(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles);

public sealed record SessionDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    string Device,
    string? IpAddress,
    bool IsCurrent);

/// <summary>Neutral acknowledgement used where the outcome must not reveal whether an account exists.</summary>
public sealed record AcceptedResponse(string Message);

public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;
}

public static class EmailPolicy
{
    public const int MaxLength = 254;
}
