using System.Collections.Concurrent;
using Microsoft.Net.Http.Headers;

namespace PersonalPlatform.TestInfrastructure;

/// <summary>
/// Minimal browser-like cookie handling for the test client that exposes the stored values, so tests can
/// replay or inspect the HttpOnly refresh cookie (something a real attacker would have to steal).
/// </summary>
public sealed class CookieJar : DelegatingHandler
{
    public const string RefreshCookieName = "__Host-pp_refresh";

    private readonly ConcurrentDictionary<string, string> _cookies = new(StringComparer.Ordinal);

    public string? RefreshToken => _cookies.TryGetValue(RefreshCookieName, out var value) ? value : null;

    public IReadOnlyList<SetCookieHeaderValue> LastSetCookies { get; private set; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_cookies.IsEmpty && !request.Headers.Contains("Cookie"))
        {
            request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            var parsed = SetCookieHeaderValue.ParseList(values.ToList());
            LastSetCookies = parsed.ToList();
            foreach (var cookie in parsed)
            {
                var expired = cookie.Expires is { } expires && expires <= DateTimeOffset.UtcNow;
                if (expired || cookie.Value.Length == 0)
                {
                    _cookies.TryRemove(cookie.Name.ToString(), out _);
                }
                else
                {
                    _cookies[cookie.Name.ToString()] = cookie.Value.ToString();
                }
            }
        }

        return response;
    }
}
