using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PersonalPlatform.E2ETests;

public sealed partial class AccountFlowTests(BrowserEnvironment env)
{
    [Fact]
    public async Task Sign_up_confirm_sign_in_survive_reload_and_sign_out()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Sign_up_confirm_sign_in_survive_reload_and_sign_out));
        var page = session.Page;
        var email = TestData.UniqueEmail("signup");

        // Sign up through the UI.
        await page.GotoAsync(session.Url("register"));
        await page.GetByLabel("Имя").FillAsync("Анна");
        await page.GetByLabel("Email").FillAsync(email);
        await page.Locator("input[type=password]").FillAsync(TestData.Password);
        await page.Locator("form button[type=submit]").ClickAsync();
        await Assertions.Expect(page.GetByText("отправили письмо")).ToBeVisibleAsync();

        // Follow the emailed link; the token travels in the fragment and is removed from the address bar.
        var mail = await env.Host.Emails.WaitForAsync(email, "confirm-email");
        var link = ConfirmLink().Match(mail.TextBody).Value;
        Assert.StartsWith(env.BaseUrl + "/confirm-email#", link, StringComparison.Ordinal);
        await page.GotoAsync(link);
        await Assertions.Expect(page.GetByText("Адрес подтверждён")).ToBeVisibleAsync();
        Assert.DoesNotContain("token", page.Url, StringComparison.OrdinalIgnoreCase);

        await session.SignInAsync(email);
        await Assertions.Expect(page.Locator("h1.greeting")).ToContainTextAsync("Анна");

        // The access token lives only in memory; the refresh token is an HttpOnly cookie, invisible to scripts.
        Assert.Equal(0, await page.EvaluateAsync<int>("() => localStorage.length + sessionStorage.length"));
        Assert.Equal(string.Empty, await page.EvaluateAsync<string>("() => document.cookie"));
        var cookie = Assert.Single(await session.Context.CookiesAsync(), c => c.Name == "__Host-pp_refresh");
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteAttribute.Strict, cookie.SameSite);

        // A reload restores the session from the refresh cookie.
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("h1.greeting")).ToBeVisibleAsync();

        // Signing out revokes the session; protected pages then require signing in again.
        await page.Locator(".sidebar").GetByRole(AriaRole.Button, new() { Name = "Выйти" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/login"));
        await page.GotoAsync(session.Url("tasks"));
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/login\\?returnUrl=%2Ftasks"));

        session.AssertHealthy();
    }

    [Fact]
    public async Task Protected_route_redirects_to_login_and_back()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Protected_route_redirects_to_login_and_back));
        var email = await session.CreateConfirmedAccountAsync();

        await session.Page.GotoAsync(session.Url("finance/transactions"));
        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("/login\\?returnUrl="));
        await session.SignInAsync(email, returnPath: "finance/transactions");

        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("/finance/transactions$"));
        session.AssertHealthy();
    }

    [Fact]
    public async Task Malicious_return_url_is_ignored_after_login()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Malicious_return_url_is_ignored_after_login));
        var email = await session.CreateConfirmedAccountAsync();

        await session.Page.GotoAsync(session.Url("login?returnUrl=%2F%2Fevil.example.test%2Fphish"));
        await session.SignInAsync(email, returnPath: "ignored");

        await Assertions.Expect(session.Page.Locator("h1.greeting")).ToBeVisibleAsync();
        Assert.StartsWith(env.BaseUrl, session.Page.Url, StringComparison.Ordinal);
        session.AssertHealthy();
    }

    [Fact]
    public async Task Wrong_password_shows_a_neutral_error()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Wrong_password_shows_a_neutral_error));
        var email = await session.CreateConfirmedAccountAsync();
        var page = session.Page;

        await page.GotoAsync(session.Url("login"));
        await page.GetByLabel("Email").FillAsync(email);
        await page.Locator("input[type=password]").FillAsync("not the right password");
        await page.Locator("form button[type=submit]").ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Неверный email или пароль");
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/login$"));
        Assert.DoesNotContain(session.ConsoleErrors, e => e.Contains("Unhandled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Host_page_is_served_with_security_headers()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Host_page_is_served_with_security_headers));

        var response = await session.Page.GotoAsync(session.Url("login"));

        var headers = await response!.AllHeadersAsync();
        Assert.Contains("frame-ancestors 'none'", headers["content-security-policy"], StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", headers["content-security-policy"], StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", headers["content-security-policy"], StringComparison.Ordinal);
        Assert.Equal("nosniff", headers["x-content-type-options"]);
        Assert.Equal("no-referrer", headers["referrer-policy"]);
        // The CSP did not block the app itself: Blazor started and rendered the page.
        await Assertions.Expect(session.Page.Locator("h1")).ToBeVisibleAsync();
        session.AssertHealthy();
    }

    [GeneratedRegex(@"http://localhost:\d+/confirm-email#\S+")]
    private static partial Regex ConfirmLink();
}
