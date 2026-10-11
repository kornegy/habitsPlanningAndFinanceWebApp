using System.Net;
using System.Text;

namespace PersonalPlatform.SecurityTests;

/// <summary>Cookies, CSRF, CORS, security headers and request size limits (scenarios 20, 21, 29).</summary>
public sealed class BrowserSecurityTests(ApiTestEnvironment env)
{
    [Fact]
    public async Task Refresh_cookie_has_host_prefix_and_strict_attributes()
    {
        var user = await env.CreateUserAsync();
        await user.RefreshAsync().EnsureStatusAsync(HttpStatusCode.OK);

        var cookie = Assert.Single(user.Cookies.LastSetCookies, c => c.Name == CookieJar.RefreshCookieName);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path.ToString());
        Assert.True(cookie.Domain.Length == 0);
    }

    [Fact]
    public async Task Access_and_refresh_tokens_are_never_both_in_the_body()
    {
        var user = await env.CreateUserAsync();
        var response = await user.RefreshAsync().EnsureStatusAsync(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(user.Cookies.RefreshToken!, body, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_without_the_csrf_header_is_rejected()
    {
        var user = await env.CreateUserAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");

        var response = await user.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_rejected", await response.ProblemCodeAsync());
    }

    [Theory]
    [InlineData("Origin", "https://evil.example")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    [InlineData("Sec-Fetch-Site", "same-site")]
    public async Task Cross_site_refresh_and_logout_are_rejected(string header, string value)
    {
        var user = await env.CreateUserAsync();
        foreach (var path in new[] { "/api/v1/auth/refresh", "/api/v1/auth/logout" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("X-CSRF", "1");
            request.Headers.Add(header, value);

            Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.SendAsync(request)).StatusCode);
        }

        // The session is still usable: the rejected cross-site logout did nothing.
        await user.RefreshAsync().EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Same_origin_refresh_is_accepted()
    {
        var user = await env.CreateUserAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("X-CSRF", "1");
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");

        Assert.Equal(HttpStatusCode.OK, (await user.Client.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/profile")]
    [InlineData("/api/v1/auth/refresh")]
    [InlineData("/api/v1/auth/login")]
    public async Task Cors_preflight_from_a_foreign_origin_is_not_approved(string path)
    {
        using var client = env.Api.CreateBareClient();
        using var preflight = new HttpRequestMessage(HttpMethod.Options, path);
        preflight.Headers.Add("Origin", "https://evil.example");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "x-csrf, content-type, authorization");

        var response = await client.SendAsync(preflight);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Simple_cross_origin_request_gets_no_cors_headers()
    {
        var user = await env.CreateUserAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/profile");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await user.Client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Api_responses_carry_security_headers_and_are_not_cacheable()
    {
        var user = await env.CreateUserAsync();
        var response = await user.GetAsync("/api/v1/profile");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Host_page_policy_forbids_inline_and_eval_scripts()
    {
        using var client = env.Api.CreateBareClient();
        var response = await client.GetAsync("/");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("*", csp, StringComparison.Ordinal);
        Assert.Equal("require-corp", response.Headers.GetValues("Cross-Origin-Embedder-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
    }

    [Theory]
    [InlineData("/robots.txt")]
    [InlineData("/does-not-exist.js")]
    [InlineData("/_framework/unknown.wasm")]
    public async Task Unknown_files_are_not_found_without_authentication_noise(string path)
    {
        using var client = env.Api.CreateBareClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_request_body_is_rejected()
    {
        var user = await env.CreateUserAsync();
        var huge = new string('a', 2 * 1024 * 1024);
        using var content = new StringContent($$"""{"title":"x","content":"{{huge}}"}""", Encoding.UTF8, "application/json");

        var response = await user.Client.PostAsync("/api/v1/notes", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("{\"email\": 42, \"password\": []}")]
    public async Task Malformed_json_is_a_400_without_internal_details(string body)
    {
        using var client = env.Api.CreateBareClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/login", content);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PersonalPlatform.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deeply_nested_json_is_rejected()
    {
        using var client = env.Api.CreateBareClient();
        var nested = new string('[', 500) + new string(']', 500);
        using var content = new StringContent($$"""{"email":"a@b.c","password":{{nested}}}""", Encoding.UTF8, "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/v1/auth/login", content)).StatusCode);
    }

    [Fact]
    public async Task Unsupported_http_methods_are_rejected()
    {
        var user = await env.CreateUserAsync();
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile");

        var response = await user.Client.SendAsync(request);

        // The JSON 404 fallback for unknown API routes also answers unsupported methods; either way the
        // request is refused without executing any action.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound });
    }
}
