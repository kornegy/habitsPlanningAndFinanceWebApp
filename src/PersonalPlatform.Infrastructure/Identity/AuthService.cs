using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalPlatform.Application.Auth;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts.Auth;
using PersonalPlatform.Domain.Users;
using PersonalPlatform.Infrastructure.Email;
using PersonalPlatform.Infrastructure.Persistence;

namespace PersonalPlatform.Infrastructure.Identity;

/// <summary>
/// Account lifecycle and session management on top of ASP.NET Core Identity.
/// </summary>
/// <remarks>
/// Anti-enumeration rules: registration, resend-confirmation and forgot-password return the same result for
/// existing and unknown addresses; login returns <c>invalid_credentials</c> for unknown users, wrong passwords and
/// locked-out accounts alike, and performs a dummy password hash for unknown users so timing is similar.
/// "email_not_confirmed" is only reported after the correct password was supplied.
/// </remarks>
public sealed partial class AuthService(
    UserManager<AppUser> users,
    AppDbContext db,
    AccessTokenIssuer tokens,
    EmailQueue emailQueue,
    AccountEmails emails,
    IOptions<SessionOptions> sessionOptions,
    IOptions<AccountEmailOptions> emailOptions,
    TimeProvider time,
    ILogger<AuthService> logger) : IAuthService
{
    // A valid PBKDF2 hash of a random password, used to equalise timing for unknown accounts.
    private static readonly Lazy<string> DummyPasswordHash = new(() =>
        new PasswordHasher<AppUser>().HashPassword(new AppUser(), Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))));

    private SessionOptions Sessions => sessionOptions.Value;

    public async Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        // Validate the password before looking the email up, so the response for a weak password does not
        // depend on whether the account exists.
        await EnsurePasswordAcceptableAsync(request.Password, request.Email);

        var now = time.GetUtcNow();
        var existing = await users.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            await SendAccountEmailIfAllowedAsync(existing, now);
            return;
        }

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = request.Email,
            Email = request.Email,
            CreatedAt = now,
            LastAccountEmailSentAt = now,
        };

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var result = await users.CreateAsync(user, request.Password);
                if (!result.Succeeded)
                {
                    if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
                    {
                        return; // Concurrent registration of the same address: stay neutral.
                    }

                    throw ToValidationException(result, nameof(RegisterRequest.Password));
                }
            }
            catch (UniqueConstraintException)
            {
                return; // Lost a race against a concurrent registration of the same address.
            }

            var locale = request.Locale is { } l && UserProfile.SupportedLocales.Contains(l) ? l : UserProfile.DefaultLocale;
            var timeZone = TimeZones.IsSupported(request.TimeZoneId) ? request.TimeZoneId! : UserProfile.DefaultTimeZone;
            var currency = locale == "en-US" ? "USD" : UserProfile.DefaultCurrency;
            using (db.ActAsOwner(user.Id))
            {
                db.UserProfiles.Add(new UserProfile(user.Id, request.DisplayName ?? string.Empty, timeZone, locale, currency, now));
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        emailQueue.Enqueue(emails.Confirmation(user.Email!, user.Id, token));
        LogUserRegistered(logger, user.Id);
    }

    public async Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            throw new AuthFailedException("invalid_token");
        }

        if (user.EmailConfirmed)
        {
            return;
        }

        var result = await users.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            throw new AuthFailedException("invalid_token");
        }
    }

    public async Task ResendConfirmationAsync(string email, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is { EmailConfirmed: false })
        {
            await SendAccountEmailIfAllowedAsync(user, time.GetUtcNow());
        }
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            new PasswordHasher<AppUser>().VerifyHashedPassword(new AppUser(), DummyPasswordHash.Value, request.Password);
            throw new AuthFailedException("invalid_credentials");
        }

        if (await users.IsLockedOutAsync(user))
        {
            LogLoginRejectedLockedOut(logger, user.Id);
            throw new AuthFailedException("invalid_credentials");
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            LogLoginFailed(logger, user.Id);
            throw new AuthFailedException("invalid_credentials");
        }

        if (!user.EmailConfirmed)
        {
            throw new AuthFailedException("email_not_confirmed");
        }

        await users.ResetAccessFailedCountAsync(user);
        return await StartSessionAsync(user, client, cancellationToken);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, ClientInfo client, CancellationToken cancellationToken)
    {
        if (!OpaqueToken.IsWellFormed(refreshToken))
        {
            throw new AuthFailedException("invalid_refresh_token");
        }

        var hash = OpaqueToken.Hash(refreshToken);
        var now = time.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Atomically claim the token. Concurrent refreshes with the same token serialise on the row lock;
        // only one can flip consumed_at from NULL, every other attempt is treated as reuse.
        var claimed = await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.ConsumedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), cancellationToken);

        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
            ?? throw new AuthFailedException("invalid_refresh_token");
        var session = await db.UserSessions.SingleAsync(s => s.Id == stored.SessionId, cancellationToken);

        if (claimed == 0)
        {
            if (stored.ConsumedAt is not null && session.IsActive(now))
            {
                // A token that was already exchanged is presented again: either the legitimate client or an
                // attacker holds a stolen copy. Revoke the whole family so neither can continue.
                session.Revoke(SessionRevocationReason.RefreshTokenReuse, now);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                LogRefreshTokenReuse(logger, session.UserId, session.Id);
            }

            throw new AuthFailedException("invalid_refresh_token");
        }

        var user = await users.FindByIdAsync(session.UserId.ToString());
        if (!session.IsActive(now) || user is null)
        {
            throw new AuthFailedException("invalid_refresh_token");
        }

        var (rawToken, replacement) = NewRefreshToken(session, now);
        stored.ReplacedById = replacement.Id;
        session.LastUsedAt = now;
        session.UserAgent = Truncate(client.UserAgent, UserSession.UserAgentMaxLength);
        session.IpAddress = Truncate(client.IpAddress, UserSession.IpAddressMaxLength);
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await BuildResultAsync(user, session, rawToken, replacement.ExpiresAt, cancellationToken);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (!OpaqueToken.IsWellFormed(refreshToken))
        {
            return;
        }

        var hash = OpaqueToken.Hash(refreshToken!);
        var sessionId = await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => (Guid?)t.SessionId).SingleOrDefaultAsync(cancellationToken);
        if (sessionId is null)
        {
            return;
        }

        await db.UserSessions
            .Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.RevokedAt, time.GetUtcNow()).SetProperty(x => x.RevocationReason, SessionRevocationReason.Logout),
                cancellationToken);
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            return;
        }

        var now = time.GetUtcNow();
        if (!user.EmailConfirmed)
        {
            await SendAccountEmailIfAllowedAsync(user, now);
            return;
        }

        if (IsThrottled(user.LastPasswordResetSentAt, now))
        {
            return;
        }

        user.LastPasswordResetSentAt = now;
        await users.UpdateAsync(user);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        emailQueue.Enqueue(emails.PasswordReset(user.Email!, user.Id, token));
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString())
            ?? throw new AuthFailedException("invalid_token");

        await EnsurePasswordAcceptableAsync(request.NewPassword, user.Email);
        var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            throw result.Errors.Any(e => e.Code == "InvalidToken")
                ? new AuthFailedException("invalid_token")
                : ToValidationException(result, nameof(ResetPasswordRequest.NewPassword));
        }

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await RevokeSessionsAsync(user.Id, exceptSessionId: null, SessionRevocationReason.PasswordReset, cancellationToken);
    }

    public async Task ChangePasswordAsync(Guid userId, Guid? currentSessionId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException();
        if (!await users.CheckPasswordAsync(user, request.CurrentPassword))
        {
            await users.AccessFailedAsync(user);
            throw new ValidationException([new ValidationFailure(nameof(request.CurrentPassword), "Current password is incorrect.") { ErrorCode = "invalid_password" }]);
        }

        await EnsurePasswordAcceptableAsync(request.NewPassword, user.Email);
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            throw ToValidationException(result, nameof(ChangePasswordRequest.NewPassword));
        }

        await RevokeSessionsAsync(user.Id, currentSessionId, SessionRevocationReason.PasswordChanged, cancellationToken);
        emailQueue.Enqueue(emails.PasswordChanged(user.Email!));
    }

    public async Task<IReadOnlyList<SessionDto>> ListSessionsAsync(Guid userId, Guid? currentSessionId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return await db.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastUsedAt)
            .Select(s => new SessionDto(s.Id, s.CreatedAt, s.LastUsedAt, s.ExpiresAt, s.UserAgent ?? "Unknown device", s.IpAddress, s.Id == currentSessionId))
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var affected = await db.UserSessions
            .Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.RevokedAt, time.GetUtcNow()).SetProperty(x => x.RevocationReason, SessionRevocationReason.RevokedByUser),
                cancellationToken);
        if (affected == 0)
        {
            throw new NotFoundException();
        }
    }

    public Task RevokeAllSessionsAsync(Guid userId, CancellationToken cancellationToken) =>
        RevokeSessionsAsync(userId, exceptSessionId: null, SessionRevocationReason.RevokedByUser, cancellationToken);

    public async Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException();
        var displayName = await db.UserProfiles.IgnoreQueryFilters()
            .Where(p => p.OwnerId == userId)
            .Select(p => p.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);
        var roles = await users.GetRolesAsync(user);
        return new CurrentUserDto(user.Id, user.Email!, displayName ?? string.Empty, roles.ToList());
    }

    public async Task DeleteAccountAsync(Guid userId, DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException();
        if (request.Confirmation != DeleteAccountRequest.RequiredConfirmation)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Confirmation), "Type DELETE to confirm.") { ErrorCode = "confirmation_required" }]);
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            throw new ValidationException([new ValidationFailure(nameof(request.Password), "Password is incorrect.") { ErrorCode = "invalid_password" }]);
        }

        // All user-owned rows reference users(id) with ON DELETE CASCADE, so this single statement removes
        // profile, sessions, tasks, habits, planner, finance and notes data atomically.
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Account deletion failed.");
        }

        LogAccountDeleted(logger, userId);
    }

    private async Task<AuthResult> StartSessionAsync(AppUser user, ClientInfo client, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await EnforceSessionLimitAsync(user.Id, now, cancellationToken);

        var session = new UserSession
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            CreatedAt = now,
            LastUsedAt = now,
            ExpiresAt = now.AddDays(Sessions.SessionMaxDays),
            UserAgent = Truncate(client.UserAgent, UserSession.UserAgentMaxLength),
            IpAddress = Truncate(client.IpAddress, UserSession.IpAddressMaxLength),
        };
        var (rawToken, refresh) = NewRefreshToken(session, now);
        db.UserSessions.Add(session);
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(cancellationToken);
        LogSessionStarted(logger, user.Id, session.Id);
        return await BuildResultAsync(user, session, rawToken, refresh.ExpiresAt, cancellationToken);
    }

    private async Task EnforceSessionLimitAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var active = await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderBy(s => s.LastUsedAt)
            .ToListAsync(cancellationToken);
        foreach (var stale in active.Take(Math.Max(0, active.Count - Sessions.MaxActiveSessionsPerUser + 1)))
        {
            stale.Revoke(SessionRevocationReason.SessionLimit, now);
        }
    }

    private (string Raw, RefreshToken Token) NewRefreshToken(UserSession session, DateTimeOffset now)
    {
        var raw = OpaqueToken.Generate();
        var expires = now.AddDays(Sessions.RefreshTokenDays);
        var token = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            SessionId = session.Id,
            TokenHash = OpaqueToken.Hash(raw),
            CreatedAt = now,
            ExpiresAt = expires < session.ExpiresAt ? expires : session.ExpiresAt,
        };
        return (raw, token);
    }

    private async Task<AuthResult> BuildResultAsync(AppUser user, UserSession session, string rawRefreshToken, DateTimeOffset refreshExpiresAt, CancellationToken cancellationToken)
    {
        var roles = await users.GetRolesAsync(user);
        var (accessToken, expiresAt) = tokens.Issue(user.Id, session.Id, roles);
        var displayName = await db.UserProfiles.IgnoreQueryFilters()
            .Where(p => p.OwnerId == user.Id)
            .Select(p => p.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);
        var current = new CurrentUserDto(user.Id, user.Email!, displayName ?? string.Empty, roles.ToList());
        return new AuthResult(new AccessTokenResponse(accessToken, expiresAt, current), rawRefreshToken, refreshExpiresAt);
    }

    private async Task RevokeSessionsAsync(Guid userId, Guid? exceptSessionId, SessionRevocationReason reason, CancellationToken cancellationToken) =>
        await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != exceptSessionId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.RevokedAt, time.GetUtcNow()).SetProperty(x => x.RevocationReason, reason),
                cancellationToken);

    private async Task SendAccountEmailIfAllowedAsync(AppUser user, DateTimeOffset now)
    {
        if (IsThrottled(user.LastAccountEmailSentAt, now))
        {
            return;
        }

        user.LastAccountEmailSentAt = now;
        await users.UpdateAsync(user);
        if (user.EmailConfirmed)
        {
            emailQueue.Enqueue(emails.AccountAlreadyExists(user.Email!));
        }
        else
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            emailQueue.Enqueue(emails.Confirmation(user.Email!, user.Id, token));
        }
    }

    private bool IsThrottled(DateTimeOffset? lastSent, DateTimeOffset now) =>
        lastSent is { } last && now - last < TimeSpan.FromSeconds(emailOptions.Value.MinSecondsBetweenEmails);

    private async Task EnsurePasswordAcceptableAsync(string password, string? email)
    {
        var probe = new AppUser { UserName = email, Email = email };
        var failures = new List<IdentityError>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, probe, password);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            throw ToValidationException(IdentityResult.Failed([.. failures]), "Password");
        }
    }

    private static ValidationException ToValidationException(IdentityResult result, string property) =>
        new(result.Errors.Select(e => new ValidationFailure(property, e.Description) { ErrorCode = e.Code }));

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength];

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} registered")]
    private static partial void LogUserRegistered(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId} started for user {UserId}")]
    private static partial void LogSessionStarted(ILogger logger, Guid userId, Guid sessionId);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "security.login_failed user={UserId}")]
    private static partial void LogLoginFailed(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning, Message = "security.login_locked_out user={UserId}")]
    private static partial void LogLoginRejectedLockedOut(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "security.refresh_token_reuse user={UserId} session={SessionId}; session revoked")]
    private static partial void LogRefreshTokenReuse(ILogger logger, Guid userId, Guid sessionId);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Information, Message = "security.account_deleted user={UserId}")]
    private static partial void LogAccountDeleted(ILogger logger, Guid userId);
}
