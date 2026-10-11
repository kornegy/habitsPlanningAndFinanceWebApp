using Microsoft.AspNetCore.Mvc;
using PersonalPlatform.Api.Security;
using PersonalPlatform.Application.Auth;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts.Auth;

namespace PersonalPlatform.Api.Controllers;

/// <summary>Account security for the signed-in user: password, sessions, deletion.</summary>
[ApiController]
[Route("api/v1/account")]
public sealed class AccountController(IAuthService auth, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("me")]
    public Task<CurrentUserDto> Me(CancellationToken cancellationToken) =>
        auth.GetCurrentUserAsync(currentUser.UserId, cancellationToken);

    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await auth.ChangePasswordAsync(currentUser.UserId, currentUser.SessionId, request, cancellationToken);
        return NoContent();
    }

    [HttpGet("sessions")]
    public Task<IReadOnlyList<SessionDto>> Sessions(CancellationToken cancellationToken) =>
        auth.ListSessionsAsync(currentUser.UserId, currentUser.SessionId, cancellationToken);

    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken)
    {
        await auth.RevokeSessionAsync(currentUser.UserId, sessionId, cancellationToken);
        if (sessionId == currentUser.SessionId)
        {
            RefreshCookie.Clear(Response);
        }

        return NoContent();
    }

    /// <summary>Signs out every device, including this one.</summary>
    [HttpPost("sessions/revoke-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeAllSessions(CancellationToken cancellationToken)
    {
        await auth.RevokeAllSessionsAsync(currentUser.UserId, cancellationToken);
        RefreshCookie.Clear(Response);
        return NoContent();
    }

    /// <summary>Permanently deletes the account and all data. Requires the password and the word DELETE.</summary>
    [HttpPost("delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        await auth.DeleteAccountAsync(currentUser.UserId, request, cancellationToken);
        RefreshCookie.Clear(Response);
        return NoContent();
    }
}
