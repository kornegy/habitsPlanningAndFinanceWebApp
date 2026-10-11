using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PersonalPlatform.Contracts.Admin;
using PersonalPlatform.Contracts.Auth;

namespace PersonalPlatform.SecurityTests;

/// <summary>Rate limiting, log hygiene, anonymous access and admin authorisation (scenarios 19, 22, 23, 30).</summary>
public sealed class AbuseAndLeakTests(ApiTestEnvironment env)
{
    [Fact]
    public async Task Login_attempts_are_rate_limited_per_client_ip_with_retry_after()
    {
        await using var api = new ApiFactory(env.Database, new Dictionary<string, string?> { ["RateLimits:AuthRequestsPerMinute"] = "3" });
        using var client = api.CreateBareClient();

        HttpResponseMessage last = null!;
        for (var i = 0; i < 4; i++)
        {
            last = await SendLoginAsync(client, "203.0.113.10");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.True(last.Headers.RetryAfter is not null);
        Assert.Equal("rate_limited", await last.ProblemCodeAsync());

        // A different client address has its own budget.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendLoginAsync(client, "203.0.113.11")).StatusCode);
    }

    [Fact]
    public async Task Spoofed_forwarded_for_header_does_not_bypass_the_limit_without_a_trusted_proxy()
    {
        await using var api = new ApiFactory(env.Database, new Dictionary<string, string?> { ["RateLimits:AuthRequestsPerMinute"] = "2" });
        using var client = api.CreateBareClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var request = LoginRequestMessage("198.51.100.7");
            request.Headers.Add("X-Forwarded-For", $"10.0.0.{i}");
            statuses.Add((await client.SendAsync(request)).StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task Forwarded_for_is_honoured_only_when_sent_by_the_configured_proxy()
    {
        await using var api = new ApiFactory(env.Database, new Dictionary<string, string?>
        {
            ["RateLimits:AuthRequestsPerMinute"] = "2",
            ["Security:KnownProxies:0"] = "192.0.2.1",
        });
        using var client = api.CreateBareClient();

        // Through the trusted proxy every forwarded client gets its own budget.
        var viaProxy = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var request = LoginRequestMessage("192.0.2.1");
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{100 + i}");
            viaProxy.Add((await client.SendAsync(request)).StatusCode);
        }

        // A direct client cannot pretend to be someone else.
        var direct = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var request = LoginRequestMessage("198.51.100.20");
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{200 + i}");
            direct.Add((await client.SendAsync(request)).StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, viaProxy);
        Assert.Contains(HttpStatusCode.TooManyRequests, direct);
    }

    [Fact]
    public async Task Authenticated_requests_are_limited_per_user_not_per_ip()
    {
        await using var api = new ApiFactory(env.Database, new Dictionary<string, string?> { ["RateLimits:UserRequestsPerMinute"] = "5" });
        var first = await TestUser.CreateAsync(api);
        var second = await TestUser.CreateAsync(api);

        var firstStatuses = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
        {
            firstStatuses.Add((await first.GetAsync("/api/v1/profile")).StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, firstStatuses);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/v1/profile")).StatusCode);
    }

    [Fact]
    public async Task Passwords_and_tokens_never_appear_in_logs()
    {
        const string password = "very secret passphrase 9137";
        var email = TestData.UniqueEmail("logcheck");
        var user = await TestUser.CreateAsync(env.Api, email, password);
        await user.RefreshAsync().EnsureStatusAsync(HttpStatusCode.OK);
        var refreshToken = user.Cookies.RefreshToken!;
        await user.PostAsync("/api/v1/account/change-password", new ChangePasswordRequest("wrong current password", "irrelevant new password"));
        await env.Api.CreateBareClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "wrong but secret attempt"), Json.Options);

        var logs = string.Join('\n', env.Api.Logs.Lines);
        Assert.NotEmpty(env.Api.Logs.Lines);
        Assert.DoesNotContain(password, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong but secret attempt", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(user.AccessToken, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(user.SessionLogin.AccessToken, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization: Bearer", logs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_api_endpoint_except_the_public_auth_ones_requires_authentication()
    {
        // Enumerate the real route table so new endpoints are covered automatically.
        var endpoints = env.Api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true)
            .Select(e => (Route: e.RoutePattern.RawText!, Methods: e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]))
            .ToList();
        var publicRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "api/v1/auth/register", "api/v1/auth/confirm-email", "api/v1/auth/resend-confirmation", "api/v1/auth/login",
            "api/v1/auth/refresh", "api/v1/auth/logout", "api/v1/auth/forgot-password", "api/v1/auth/reset-password",
            "api/v1/profile/reference-data", "api/{**path}",
        };

        Assert.True(endpoints.Count > 15);
        using var anonymous = env.Api.CreateBareClient();
        foreach (var (route, methods) in endpoints.Where(e => !publicRoutes.Contains(e.Route)))
        {
            var url = "/" + RouteValues.Fill(route);
            foreach (var method in methods)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), url);
                if (method is "POST" or "PUT")
                {
                    request.Content = JsonContent.Create(new { });
                }

                var response = await anonymous.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {url} returned {(int)response.StatusCode}");
            }
        }
    }

    [Fact]
    public async Task Admin_endpoints_are_forbidden_for_regular_users_and_allowed_for_admins()
    {
        var user = await env.CreateUserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/admin/stats")).StatusCode);

        // A forged admin role claim does not help: the token signature check fails.
        var forged = TokenForge.Create(user.Id, TokenForge.SessionIdOf(user.AccessToken), key: new byte[32], roles: [Roles.Admin]);
        using var forgedClient = env.Api.CreateBareClient();
        forgedClient.DefaultRequestHeaders.Authorization = new("Bearer", forged);
        Assert.Equal(HttpStatusCode.Unauthorized, (await forgedClient.GetAsync("/api/v1/admin/stats")).StatusCode);

        var admin = await env.CreateUserAsync();
        await using (var db = env.Database.CreateOwnerContext())
        {
            db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = admin.Id, RoleId = new Guid("0195f3a0-0000-7000-8000-000000000001") });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var login = await TestUser.LoginAsync(admin.Client, admin.Email);
        admin.UseAccessToken(login.AccessToken);
        var stats = await admin.GetJsonAsync<AdminStatsDto>("/api/v1/admin/stats");
        Assert.True(stats.Users >= 2);
    }

    private static async Task<HttpResponseMessage> SendLoginAsync(HttpClient client, string ip)
    {
        using var request = LoginRequestMessage(ip);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage LoginRequestMessage(string ip)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(TestData.UniqueEmail(), "some wrong password"), options: Json.Options),
        };
        request.Headers.Add(ApiFactory.TestClientIpHeader, ip);
        return request;
    }
}

internal static class RouteValues
{
    /// <summary>Replaces route parameters with plausible values ({id:guid} → a random GUID, {date} → a date).</summary>
    public static string Fill(string route) =>
        System.Text.RegularExpressions.Regex.Replace(route, @"\{(?<name>[^}:]+)(:(?<constraint>[^}]+))?\}", match =>
        {
            var constraint = match.Groups["constraint"].Value;
            var name = match.Groups["name"].Value;
            if (constraint.StartsWith("guid", StringComparison.Ordinal) || name.EndsWith("Id", StringComparison.Ordinal) || name == "id")
            {
                return Guid.NewGuid().ToString();
            }

            return name.Contains("date", StringComparison.OrdinalIgnoreCase) ? "2026-01-15" : "1";
        });
}
