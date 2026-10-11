using PersonalPlatform.Contracts.Auth;

namespace PersonalPlatform.Application.Auth;

/// <summary>Request metadata recorded with a session (shown to the user in the session list).</summary>
public sealed record ClientInfo(string? UserAgent, string? IpAddress);

/// <summary>
/// Successful sign-in or refresh. <see cref="RefreshToken"/> is the raw opaque token; the API places it only in
/// an HttpOnly cookie and never in a response body or log.
/// </summary>
public sealed record AuthResult(AccessTokenResponse Response, string RefreshToken, DateTimeOffset RefreshExpiresAt);

/// <summary>Authentication failed. <see cref="Code"/> is deliberately coarse to avoid account enumeration.</summary>
public sealed class AuthFailedException : Exception
{
    public AuthFailedException(string code)
        : base("Authentication failed.")
    {
        Code = code;
    }

    public AuthFailedException()
        : this("invalid_credentials")
    {
    }

    public AuthFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "invalid_credentials";
    }

    public string Code { get; }
}

public interface IAuthService
{
    /// <summary>Always completes the same way whether or not the email is already registered.</summary>
    Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken);

    Task ResendConfirmationAsync(string email, CancellationToken cancellationToken);

    Task<AuthResult> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken cancellationToken);

    /// <summary>Rotates the refresh token. Reuse of an already rotated token revokes the whole session.</summary>
    Task<AuthResult> RefreshAsync(string refreshToken, ClientInfo client, CancellationToken cancellationToken);

    /// <summary>Revokes the session that owns the token, if any. Never fails for unknown tokens.</summary>
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken);

    Task ForgotPasswordAsync(string email, CancellationToken cancellationToken);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);

    /// <summary>Changes the password and revokes every session except the current one.</summary>
    Task ChangePasswordAsync(Guid userId, Guid? currentSessionId, ChangePasswordRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionDto>> ListSessionsAsync(Guid userId, Guid? currentSessionId, CancellationToken cancellationToken);

    Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Revokes all sessions of the user, including the current one.</summary>
    Task RevokeAllSessionsAsync(Guid userId, CancellationToken cancellationToken);

    Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Permanently deletes the account and all owned data (cascade). Requires the password.</summary>
    Task DeleteAccountAsync(Guid userId, DeleteAccountRequest request, CancellationToken cancellationToken);
}
