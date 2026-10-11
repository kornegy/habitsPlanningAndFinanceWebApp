using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace PersonalPlatform.Client.Auth;

/// <summary>
/// UI-only view of the session for routing and conditional rendering. It grants nothing: every API call is
/// authorised by the server.
/// </summary>
public sealed class AppAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private readonly AuthSession _session;
    private Task<bool>? _restore;

    public AppAuthenticationStateProvider(AuthSession session)
    {
        _session = session;
        _session.Changed += OnChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!_session.IsAuthenticated)
        {
            // First load or reload: try to restore the session from the refresh cookie once.
            _restore ??= _session.RefreshAsync();
            await _restore;
        }

        return Build();
    }

    public void Dispose() => _session.Changed -= OnChanged;

    private AuthenticationState Build()
    {
        if (_session.User is not { } user)
        {
            return Anonymous;
        }

        var claims = new List<Claim> { new("sub", user.Id.ToString()), new(ClaimTypes.Name, user.DisplayName) };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt")));
    }

    private void OnChanged() => NotifyAuthenticationStateChanged(Task.FromResult(Build()));
}
