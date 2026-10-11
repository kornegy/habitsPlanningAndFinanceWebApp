using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PersonalPlatform.Api.Infrastructure;
using PersonalPlatform.Api.Security;
using PersonalPlatform.Application.Auth;
using PersonalPlatform.Contracts.Auth;

namespace PersonalPlatform.Api.Controllers;

/// <summary>Public authentication endpoints. All are strictly rate limited per client IP.</summary>
[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    private static readonly AcceptedResponse CheckEmail =
        new("If the address can be used, we have sent an email with further instructions.");

    [HttpPost("register")]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType<AcceptedResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        await auth.RegisterAsync(request, cancellationToken);
        return Accepted(CheckEmail);
    }

    [HttpPost("confirm-email")]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        await auth.ConfirmEmailAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("resend-confirmation")]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType<AcceptedResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendConfirmation(EmailRequest request, CancellationToken cancellationToken)
    {
        await auth.ResendConfirmationAsync(request.Email, cancellationToken);
        return Accepted(CheckEmail);
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccessTokenResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request, ClientInfoOf(HttpContext), cancellationToken);
        RefreshCookie.Write(Response, result.RefreshToken, result.RefreshExpiresAt);
        return result.Response;
    }

    /// <summary>Exchanges the refresh cookie for a new access token and a rotated refresh cookie.</summary>
    [HttpPost("refresh")]
    [RequireCsrfHeader]
    [EnableRateLimiting(RateLimiting.Refresh)]
    [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccessTokenResponse>> Refresh(CancellationToken cancellationToken)
    {
        var token = RefreshCookie.Read(Request);
        if (token is null)
        {
            throw new AuthFailedException("invalid_refresh_token");
        }

        try
        {
            var result = await auth.RefreshAsync(token, ClientInfoOf(HttpContext), cancellationToken);
            RefreshCookie.Write(Response, result.RefreshToken, result.RefreshExpiresAt);
            return result.Response;
        }
        catch (AuthFailedException)
        {
            // A rejected refresh cookie is useless; remove it so the client stops presenting it.
            RefreshCookie.Clear(Response);
            throw;
        }
    }

    [HttpPost("logout")]
    [RequireCsrfHeader]
    [EnableRateLimiting(RateLimiting.Refresh)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(RefreshCookie.Read(Request), cancellationToken);
        RefreshCookie.Clear(Response);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType<AcceptedResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken cancellationToken)
    {
        await auth.ForgotPasswordAsync(request.Email, cancellationToken);
        return Accepted(CheckEmail);
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await auth.ResetPasswordAsync(request, cancellationToken);
        return NoContent();
    }

    internal static ClientInfo ClientInfoOf(HttpContext context) => new(
        context.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null,
        context.Connection.RemoteIpAddress?.ToString());
}
