using System.Net.Http.Json;
using Microsoft.Playwright;
using PersonalPlatform.Contracts.Auth;

namespace PersonalPlatform.E2ETests;

/// <summary>
/// A fresh browser profile (context) for one test. It records console errors, uncaught page errors and every
/// JavaScript dialog from the very first navigation, so injected script would be noticed even if it ran early.
/// A Playwright trace is written to <c>playwright-traces/</c> for debugging.
/// </summary>
public sealed class BrowserSession : IAsyncDisposable
{
    private static readonly string TraceDirectory = Path.Combine(AppContext.BaseDirectory, "playwright-traces");

    private readonly BrowserEnvironment _env;
    private readonly string _traceName;

    private BrowserSession(BrowserEnvironment env, IBrowserContext context, IPage page, string traceName)
    {
        _env = env;
        Context = context;
        Page = page;
        _traceName = traceName;
        page.Console += (_, message) =>
        {
            // A 401 from the silent session restore on first load is expected for signed-out visitors.
            if (message.Type == "error" && !message.Text.Contains("401", StringComparison.Ordinal))
            {
                ConsoleErrors.Add(message.Text);
            }
        };
        page.PageError += (_, error) => ConsoleErrors.Add(error);
        page.Dialog += async (_, dialog) =>
        {
            Dialogs.Add(dialog.Message);
            await dialog.DismissAsync();
        };
    }

    public IBrowserContext Context { get; }

    public IPage Page { get; }

    public List<string> ConsoleErrors { get; } = [];

    public List<string> Dialogs { get; } = [];

    public string Url(string path) => _env.BaseUrl + "/" + path.TrimStart('/');

    public static async Task<BrowserSession> StartAsync(BrowserEnvironment env, string name, ViewportSize? viewport = null)
    {
        Assertions.SetDefaultExpectTimeout(15_000);
        var context = await env.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "ru-RU",
            TimezoneId = "Europe/Moscow",
            ViewportSize = viewport ?? new ViewportSize { Width = 1280, Height = 860 },
        });
        context.SetDefaultTimeout(20_000);
        await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true });
        var page = await context.NewPageAsync();
        return new BrowserSession(env, context, page, name);
    }

    /// <summary>Creates a confirmed account through the API (fast path for tests that are not about sign-up).</summary>
    public async Task<string> CreateConfirmedAccountAsync()
    {
        var email = TestData.UniqueEmail("e2e");
        using var http = new HttpClient { BaseAddress = new Uri(_env.BaseUrl) };
        var register = await http.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, TestData.Password, "Тест", "Europe/Moscow", "ru-RU"));
        register.EnsureSuccessStatusCode();
        var mail = await _env.Host.Emails.WaitForAsync(email, "confirm-email");
        var (userId, token) = CapturingEmailSender.ParseLink(mail, "confirm-email");
        var confirm = await http.PostAsJsonAsync("/api/v1/auth/confirm-email", new ConfirmEmailRequest(userId, token));
        confirm.EnsureSuccessStatusCode();
        return email;
    }

    public async Task SignInAsync(string email, string? returnPath = null)
    {
        if (returnPath is null)
        {
            await Page.GotoAsync(Url("login"));
        }

        await Page.GetByLabel("Email").FillAsync(email);
        await Page.Locator("input[type=password]").FillAsync(TestData.Password);
        await Page.Locator("form button[type=submit]").ClickAsync();
    }

    public async Task<string> SignInAsNewUserAsync()
    {
        var email = await CreateConfirmedAccountAsync();
        await SignInAsync(email);
        await Assertions.Expect(Page.Locator("h1.greeting")).ToBeVisibleAsync();
        return email;
    }

    /// <summary>Submits the currently open modal form.</summary>
    public Task SubmitModalAsync() => Page.Locator(".modal-footer button[type=submit]").ClickAsync();

    /// <summary>Fails the test if the page logged errors or opened a dialog (e.g. injected script ran).</summary>
    public void AssertHealthy()
    {
        Assert.Empty(Dialogs);
        Assert.Empty(ConsoleErrors);
    }

    public async ValueTask DisposeAsync()
    {
        Directory.CreateDirectory(TraceDirectory);
        await Context.Tracing.StopAsync(new TracingStopOptions { Path = Path.Combine(TraceDirectory, _traceName + ".zip") });
        await Context.DisposeAsync();
    }
}
