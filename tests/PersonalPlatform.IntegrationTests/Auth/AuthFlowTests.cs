using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Contracts.Auth;
using PersonalPlatform.Contracts.Profile;

namespace PersonalPlatform.IntegrationTests.Auth;

public sealed class AuthFlowTests(ApiTestEnvironment env)
{
    private ApiFactory Api => env.Api;

    [Fact]
    public async Task Register_confirm_login_returns_access_token_and_refresh_cookie()
    {
        var user = await env.CreateUserAsync();

        var me = await user.GetJsonAsync<CurrentUserDto>("/api/v1/account/me");
        Assert.Equal(user.Id, me.Id);
        Assert.Equal(user.Email, me.Email);
        Assert.Empty(me.Roles);

        var profile = await user.GetJsonAsync<ProfileDto>("/api/v1/profile");
        Assert.Equal("Europe/Moscow", profile.TimeZoneId);
        Assert.Equal("RUB", profile.CurrencyCode);
    }

    [Fact]
    public async Task Registering_an_existing_email_is_indistinguishable_and_notifies_the_owner()
    {
        var user = await env.CreateUserAsync();
        var client = Api.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(user.Email, "another long password", null, null, null), Json.Options);
        var fresh = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(TestData.UniqueEmail(), "another long password", null, null, null), Json.Options);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(await fresh.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        await Api.Emails.WaitForAsync(user.Email, "/forgot-password");
        await using var db = env.Database.CreateOwnerContext();
        Assert.Equal(1, await db.Users.CountAsync(u => u.NormalizedEmail == user.Email.ToUpperInvariant()));
    }

    [Fact]
    public async Task Weak_password_is_rejected_whether_or_not_the_email_exists()
    {
        var user = await env.CreateUserAsync();
        var client = Api.CreateHttpsClient();

        var existing = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(user.Email, "short", null, null, null), Json.Options);
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(TestData.UniqueEmail(), "short", null, null, null), Json.Options);

        Assert.Equal(HttpStatusCode.BadRequest, existing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Login_failures_are_neutral_and_unconfirmed_email_is_reported_only_with_correct_password()
    {
        var client = Api.CreateHttpsClient();
        var email = TestData.UniqueEmail();
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, TestData.Password, null, null, null), Json.Options)
            .EnsureStatusAsync(HttpStatusCode.Accepted);

        var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "wrong password 123"), Json.Options);
        var unknownUser = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(TestData.UniqueEmail(), "wrong password 123"), Json.Options);
        var unconfirmed = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, TestData.Password), Json.Options);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal("invalid_credentials", await wrongPassword.ProblemCodeAsync());
        Assert.Equal("invalid_credentials", await unknownUser.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, unconfirmed.StatusCode);
        Assert.Equal("email_not_confirmed", await unconfirmed.ProblemCodeAsync());
        Assert.False(unknownUser.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Account_is_locked_after_repeated_failures_even_for_the_correct_password()
    {
        var user = await env.CreateUserAsync();
        var client = Api.CreateHttpsClient();
        for (var i = 0; i < 8; i++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(user.Email, $"wrong password {i}"), Json.Options);
        }

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(user.Email, TestData.Password), Json.Options);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_credentials", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_issues_a_new_access_token()
    {
        var user = await env.CreateUserAsync();

        var first = await user.RefreshAsync().EnsureStatusAsync(HttpStatusCode.OK);
        var firstToken = await first.ReadJsonAsync<AccessTokenResponse>();
        var second = await user.RefreshAsync().EnsureStatusAsync(HttpStatusCode.OK);
        var secondToken = await second.ReadJsonAsync<AccessTokenResponse>();

        Assert.NotEqual(firstToken.AccessToken, secondToken.AccessToken);
        Assert.Equal(TokenForge.SessionIdOf(user.AccessToken), TokenForge.SessionIdOf(secondToken.AccessToken));
        user.UseAccessToken(secondToken.AccessToken);
        await user.GetAsync("/api/v1/account/me").EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_session()
    {
        var user = await env.CreateUserAsync();
        var stolenCookie = RefreshCookieOf(user);

        await user.RefreshAsync().EnsureStatusAsync(HttpStatusCode.OK); // legitimate rotation

        // Attacker replays the old token.
        var replay = await SendRefreshWithCookieAsync(stolenCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // The legitimate client's current token is now dead too, and so is its access token.
        var legit = await user.RefreshAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, legit.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/v1/account/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_stops_refresh_and_invalidates_the_access_token_immediately()
    {
        var user = await env.CreateUserAsync();
        var cookie = RefreshCookieOf(user);

        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout"))
        {
            request.Headers.Add("X-CSRF", "1");
            await user.Client.SendAsync(request).EnsureStatusAsync(HttpStatusCode.NoContent);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendRefreshWithCookieAsync(cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/v1/profile")).StatusCode);
    }

    [Fact]
    public async Task Sessions_can_be_listed_and_revoked_individually()
    {
        var user = await env.CreateUserAsync();
        var otherDevice = Api.CreateHttpsClient();
        var other = await TestUser.LoginAsync(otherDevice, user.Email);
        var otherSession = TokenForge.SessionIdOf(other.AccessToken);

        var sessions = await user.GetJsonAsync<List<SessionDto>>("/api/v1/account/sessions");
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, s => s.IsCurrent);

        await user.DeleteAsync($"/api/v1/account/sessions/{otherSession}").EnsureStatusAsync(HttpStatusCode.NoContent);

        otherDevice.DefaultRequestHeaders.Authorization = new("Bearer", other.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/v1/profile")).StatusCode);
        await user.GetAsync("/api/v1/profile").EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.NotFound, (await user.DeleteAsync($"/api/v1/account/sessions/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Revoke_all_signs_out_every_device_including_the_current_one()
    {
        var user = await env.CreateUserAsync();
        var otherDevice = Api.CreateHttpsClient();
        var other = await TestUser.LoginAsync(otherDevice, user.Email);

        await user.PostAsync("/api/v1/account/sessions/revoke-all", new { }).EnsureStatusAsync(HttpStatusCode.NoContent);

        otherDevice.DefaultRequestHeaders.Authorization = new("Bearer", other.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/v1/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/v1/profile")).StatusCode);
    }

    [Fact]
    public async Task Changing_password_revokes_other_sessions_but_keeps_the_current_one()
    {
        var user = await env.CreateUserAsync();
        var otherDevice = Api.CreateHttpsClient();
        var other = await TestUser.LoginAsync(otherDevice, user.Email);
        const string newPassword = "a brand new long passphrase";

        var wrong = await user.PostAsync("/api/v1/account/change-password", new ChangePasswordRequest("not my password", newPassword));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        await user.PostAsync("/api/v1/account/change-password", new ChangePasswordRequest(TestData.Password, newPassword))
            .EnsureStatusAsync(HttpStatusCode.NoContent);

        otherDevice.DefaultRequestHeaders.Authorization = new("Bearer", other.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/v1/profile")).StatusCode);
        await user.GetAsync("/api/v1/profile").EnsureStatusAsync(HttpStatusCode.OK);
        await TestUser.LoginAsync(Api.CreateHttpsClient(), user.Email, newPassword);
        await Api.Emails.WaitForAsync(user.Email, "Your account password was changed");
    }

    [Fact]
    public async Task Password_reset_sets_a_new_password_and_signs_out_everywhere()
    {
        var user = await env.CreateUserAsync();
        var client = Api.CreateHttpsClient();
        const string newPassword = "reset to another passphrase";

        await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new EmailRequest(user.Email), Json.Options)
            .EnsureStatusAsync(HttpStatusCode.Accepted);
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new EmailRequest(TestData.UniqueEmail()), Json.Options);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);

        var mail = await Api.Emails.WaitForAsync(user.Email, "/reset-password#");
        var (userId, token) = CapturingEmailSender.ParseLink(mail, "reset-password");

        var badToken = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(userId, "garbage", newPassword), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, badToken.StatusCode);

        await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(userId, token, newPassword), Json.Options)
            .EnsureStatusAsync(HttpStatusCode.NoContent);

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/v1/profile")).StatusCode);
        await TestUser.LoginAsync(Api.CreateHttpsClient(), user.Email, newPassword);

        // The token is single-use: the password change rotated the security stamp.
        var reuse = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(userId, token, "yet another passphrase"), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
    }

    [Fact]
    public async Task Exceeding_the_session_limit_revokes_the_least_recently_used_session()
    {
        var user = await env.CreateUserAsync(); // session #1
        for (var i = 0; i < 10; i++)
        {
            await TestUser.LoginAsync(Api.CreateHttpsClient(), user.Email);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/v1/profile")).StatusCode);
        await using var db = env.Database.CreateOwnerContext();
        Assert.Equal(10, await db.UserSessions.CountAsync(s => s.UserId == user.Id && s.RevokedAt == null));
    }

    [Fact]
    public async Task Deleting_the_account_requires_password_and_removes_all_user_data()
    {
        var user = await env.CreateUserAsync();

        var wrong = await user.PostAsync("/api/v1/account/delete", new DeleteAccountRequest("wrong password!!", "DELETE"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var unconfirmed = await user.PostAsync("/api/v1/account/delete", new DeleteAccountRequest(TestData.Password, "delete"));
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);

        await user.PostAsync("/api/v1/account/delete", new DeleteAccountRequest(TestData.Password, "DELETE"))
            .EnsureStatusAsync(HttpStatusCode.NoContent);

        await using var db = env.Database.CreateOwnerContext();
        Assert.False(await db.Users.AnyAsync(u => u.Id == user.Id));
        Assert.False(await db.UserProfiles.IgnoreQueryFilters().AnyAsync(p => p.OwnerId == user.Id));
        Assert.False(await db.UserSessions.AnyAsync(s => s.UserId == user.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/v1/profile")).StatusCode);
    }

    private static string RefreshCookieOf(TestUser user) =>
        user.Cookies.RefreshToken ?? throw new InvalidOperationException("No refresh cookie.");

    private async Task<HttpResponseMessage> SendRefreshWithCookieAsync(string cookieValue)
    {
        var client = Api.CreateBareClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("X-CSRF", "1");
        request.Headers.Add("Cookie", $"__Host-pp_refresh={cookieValue}");
        return await client.SendAsync(request);
    }
}
