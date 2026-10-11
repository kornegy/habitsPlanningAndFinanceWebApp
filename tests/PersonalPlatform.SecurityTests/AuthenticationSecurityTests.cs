using System.Net;
using System.Net.Http.Headers;
using Microsoft.IdentityModel.Tokens;

namespace PersonalPlatform.SecurityTests;

/// <summary>
/// Access-token validation: signature, issuer, audience, lifetime, algorithm allow-list and session binding.
/// Scenarios 12-19 of the security test plan.
/// </summary>
public sealed class AuthenticationSecurityTests(ApiTestEnvironment env)
{
    [Fact]
    public async Task Valid_forged_baseline_token_for_an_active_session_is_accepted()
    {
        // Guards the negative tests below: a correctly built token with the real key does work.
        var user = await env.CreateUserAsync();
        var token = TokenForge.Create(user.Id, TokenForge.SessionIdOf(user.AccessToken));

        Assert.Equal(HttpStatusCode.OK, (await GetProfileAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var user = await env.CreateUserAsync();
        var token = TokenForge.Create(user.Id, TokenForge.SessionIdOf(user.AccessToken), expires: DateTimeOffset.UtcNow.AddMinutes(-2));

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetProfileAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Token_signed_with_a_different_key_is_rejected()
    {
        var user = await env.CreateUserAsync();
        var otherKey = Enumerable.Range(0, 32).Select(i => (byte)(255 - i)).ToArray();
        var token = TokenForge.Create(user.Id, TokenForge.SessionIdOf(user.AccessToken), key: otherKey);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetProfileAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Tampered_payload_is_rejected()
    {
        var user = await env.CreateUserAsync();
        var victim = await env.CreateUserAsync();
        var parts = user.AccessToken.Split('.');
        var payload = Base64UrlEncoder.Decode(parts[1]).Replace(user.Id.ToString(), victim.Id.ToString(), StringComparison.Ordinal);
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetProfileAsync(tampered)).StatusCode);
    }

    [Theory]
    [InlineData("wrong-issuer", ApiFactory.Audience)]
    [InlineData(ApiFactory.Issuer, "wrong-audience")]
    public async Task Wrong_issuer_or_audience_is_rejected(string issuer, string audience)
    {
        var user = await env.CreateUserAsync();
        var token = TokenForge.Create(user.Id, TokenForge.SessionIdOf(user.AccessToken), issuer: issuer, audience: audience);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetProfileAsync(token)).StatusCode);
    }

    [Theory]
    [InlineData(SecurityAlgorithms.HmacSha384)]
    [InlineData(SecurityAlgorithms.HmacSha512)]
    public async Task Algorithms_outside_the_allow_list_are_rejected_even_with_the_right_key(string algorithm)
    {
        var user = await env.CreateUserAsync();
        var token = TokenForge.Create(user.Id, TokenForge.SessionIdOf(user.AccessToken), key: [.. ApiFactory.SigningKey, .. ApiFactory.SigningKey], algorithm: algorithm);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetProfileAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Unsigned_alg_none_token_is_rejected()
    {
        var user = await env.CreateUserAsync();
        var token = TokenForge.Unsigned(user.Id, TokenForge.SessionIdOf(user.AccessToken));

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetProfileAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Correctly_signed_token_for_an_unknown_or_foreign_session_is_rejected()
    {
        var user = await env.CreateUserAsync();
        var other = await env.CreateUserAsync();

        var unknownSession = TokenForge.Create(user.Id, Guid.NewGuid());
        var foreignSession = TokenForge.Create(user.Id, TokenForge.SessionIdOf(other.AccessToken));

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetProfileAsync(unknownSession)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetProfileAsync(foreignSession)).StatusCode);
    }

    [Fact]
    public async Task Revoked_session_cannot_refresh_or_call_the_api()
    {
        var user = await env.CreateUserAsync();
        var cookie = user.Cookies.RefreshToken!;
        await user.PostAsync("/api/v1/account/sessions/revoke-all", new { }).EnsureStatusAsync(HttpStatusCode.NoContent);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshWithCookieAsync(cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/v1/profile")).StatusCode);
    }

    [Fact]
    public async Task Replayed_refresh_token_revokes_the_family()
    {
        var user = await env.CreateUserAsync();
        var original = user.Cookies.RefreshToken!;
        await user.RefreshAsync().EnsureStatusAsync(HttpStatusCode.OK);
        var rotated = user.Cookies.RefreshToken!;
        Assert.NotEqual(original, rotated);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshWithCookieAsync(original)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshWithCookieAsync(rotated)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_refreshes_with_one_token_never_yield_two_valid_tokens()
    {
        var user = await env.CreateUserAsync();
        var token = user.Cookies.RefreshToken!;

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RefreshWithCookieAsync(token)));

        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.OK) <= 1);
    }

    [Fact]
    public async Task Logout_ends_the_ability_to_refresh()
    {
        var user = await env.CreateUserAsync();
        var cookie = user.Cookies.RefreshToken!;
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout"))
        {
            request.Headers.Add("X-CSRF", "1");
            await user.Client.SendAsync(request).EnsureStatusAsync(HttpStatusCode.NoContent);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshWithCookieAsync(cookie)).StatusCode);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("Bearer")]
    [InlineData("Basic dXNlcjpwYXNz")]
    public async Task Malformed_authorization_headers_are_rejected(string header)
    {
        using var client = env.Api.CreateBareClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/profile");
        request.Headers.TryAddWithoutValidation("Authorization", header);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    private async Task<HttpResponseMessage> GetProfileAsync(string token)
    {
        using var client = env.Api.CreateBareClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync("/api/v1/profile");
    }

    private async Task<HttpResponseMessage> RefreshWithCookieAsync(string cookie)
    {
        using var client = env.Api.CreateBareClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("X-CSRF", "1");
        request.Headers.Add("Cookie", $"{CookieJar.RefreshCookieName}={cookie}");
        return await client.SendAsync(request);
    }
}
