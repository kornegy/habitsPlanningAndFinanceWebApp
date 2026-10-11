namespace PersonalPlatform.Api.Security;

/// <summary>
/// The refresh token cookie.
/// <list type="bullet">
/// <item><c>__Host-</c> prefix: the browser only accepts it with Secure, Path=/ and no Domain, so subdomains
/// cannot set or overwrite it.</item>
/// <item><c>HttpOnly</c>: unreadable by JavaScript (and by the WASM app); an XSS cannot exfiltrate it, although it
/// could still make requests while the page is open, which is why CSP and output encoding remain essential.</item>
/// <item><c>SameSite=Strict</c>: never sent on cross-site requests; together with the mandatory X-CSRF header and
/// Origin checks this prevents CSRF against refresh/logout.</item>
/// </list>
/// </summary>
public static class RefreshCookie
{
    public const string Name = "__Host-pp_refresh";

    public static void Write(HttpResponse response, string token, DateTimeOffset expiresAt) =>
        response.Cookies.Append(Name, token, Options(expiresAt));

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, Options(DateTimeOffset.UnixEpoch));

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) ? value : null;

    private static CookieOptions Options(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiresAt,
        IsEssential = true,
    };
}
