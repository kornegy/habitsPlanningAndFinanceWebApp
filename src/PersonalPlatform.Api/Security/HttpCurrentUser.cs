using PersonalPlatform.Application.Common;
using PersonalPlatform.Infrastructure.Identity;

namespace PersonalPlatform.Api.Security;

/// <summary>Current user taken exclusively from the validated access token of the current request.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => TryGetGuid(AppClaims.Subject) is not null;

    public Guid UserId => TryGetGuid(AppClaims.Subject)
        ?? throw new InvalidOperationException("No authenticated user in the current context.");

    public Guid? SessionId => TryGetGuid(AppClaims.SessionId);

    private Guid? TryGetGuid(string claimType)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return Guid.TryParse(principal.FindFirst(claimType)?.Value, out var value) && value != Guid.Empty ? value : null;
    }
}
