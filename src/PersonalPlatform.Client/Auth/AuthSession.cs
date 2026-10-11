using System.Text.Json;
using Microsoft.JSInterop;
using PersonalPlatform.Client.Services;
using PersonalPlatform.Contracts.Auth;

namespace PersonalPlatform.Client.Auth;

/// <summary>
/// Holds the access token in memory only (never in localStorage/sessionStorage). On page load the session is
/// restored through the refresh endpoint, which uses the HttpOnly refresh cookie.
/// </summary>
public sealed class AuthSession(IJSRuntime js) : IAsyncDisposable
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(45);

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private IJSObjectReference? _module;
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public event Action? Changed;

    public CurrentUserDto? User { get; private set; }

    public bool IsAuthenticated => _accessToken is not null;

    public void SignIn(AccessTokenResponse response)
    {
        _accessToken = response.AccessToken;
        _expiresAt = response.ExpiresAt;
        User = response.User;
        Changed?.Invoke();
    }

    /// <summary>Returns a valid access token, refreshing it shortly before expiry. Null when signed out.</summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        if (_accessToken is not null && _expiresAt - DateTimeOffset.UtcNow > RefreshMargin)
        {
            return _accessToken;
        }

        return await RefreshAsync() ? _accessToken : null;
    }

    /// <summary>
    /// Exchanges the refresh cookie for a new access token. Concurrent callers in this tab share one request;
    /// other tabs are serialised by a Web Lock in session.js.
    /// </summary>
    public async Task<bool> RefreshAsync(string? rejectedToken = null)
    {
        var observed = rejectedToken ?? _accessToken;
        await _refreshGate.WaitAsync();
        try
        {
            if (_accessToken is not null && _accessToken != observed && _expiresAt - DateTimeOffset.UtcNow > RefreshMargin)
            {
                return true; // another caller refreshed while we waited
            }

            var module = await ModuleAsync();
            var result = await module.InvokeAsync<JsResponse>("refresh");
            if (result.Status == 200)
            {
                var response = JsonSerializer.Deserialize<AccessTokenResponse>(result.Body, ApiJson.Options)!;
                SignIn(response);
                return true;
            }

            Clear();
            return false;
        }
        catch (JSException)
        {
            Clear();
            return false;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task SignOutAsync()
    {
        try
        {
            var module = await ModuleAsync();
            await module.InvokeAsync<JsResponse>("logout");
        }
        finally
        {
            Clear();
        }
    }

    /// <summary>Forgets the in-memory token and user (e.g. after the server rejected the session).</summary>
    public void Clear()
    {
        var wasAuthenticated = IsAuthenticated;
        _accessToken = null;
        _expiresAt = default;
        User = null;
        if (wasAuthenticated)
        {
            Changed?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }

        _refreshGate.Dispose();
    }

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/session.js");

    private sealed record JsResponse(int Status, string Body);
}
