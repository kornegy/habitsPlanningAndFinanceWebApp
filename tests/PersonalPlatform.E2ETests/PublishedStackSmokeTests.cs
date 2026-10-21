using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PersonalPlatform.E2ETests;

/// <summary>
/// Smoke test against a running Docker deployment (the published, trimmed client) instead of the in-process test
/// host, which serves an untrimmed build. Runs when PP_SMOKE_BASE_URL and PP_SMOKE_MAILPIT_URL are set, e.g. in the
/// DAST job against <c>docker compose</c>; other runs skip it with that reason.
/// </summary>
public sealed partial class PublishedStackSmokeTests(BrowserEnvironment env)
{
    private static readonly string? BaseUrl = Environment.GetEnvironmentVariable("PP_SMOKE_BASE_URL")?.TrimEnd('/');
    private static readonly string? MailpitUrl = Environment.GetEnvironmentVariable("PP_SMOKE_MAILPIT_URL")?.TrimEnd('/');

    [Fact]
    public async Task Published_client_signs_up_signs_in_and_saves_data()
    {
        Assert.SkipWhen(BaseUrl is null || MailpitUrl is null, "PP_SMOKE_BASE_URL and PP_SMOKE_MAILPIT_URL are not set (no deployed stack).");

        await using var context = await env.Browser.NewContextAsync(new BrowserNewContextOptions { Locale = "ru-RU" });
        context.SetDefaultTimeout(30_000);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);
        page.Console += (_, m) =>
        {
            if (m.Type == "error" && m.Text.Contains("Unhandled", StringComparison.Ordinal))
            {
                errors.Add(m.Text);
            }
        };
        var email = TestData.UniqueEmail("smoke");

        await page.GotoAsync(BaseUrl + "/register");
        await page.GetByLabel("Email").FillAsync(email);
        await page.Locator("input[type=password]").FillAsync(TestData.Password);
        await page.Locator("form button[type=submit]").ClickAsync();
        await Assertions.Expect(page.GetByText("отправили письмо")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await page.GotoAsync(await ConfirmationLinkAsync(email));
        await Assertions.Expect(page.GetByText("Адрес подтверждён")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await page.GotoAsync(BaseUrl + "/login");
        await page.GetByLabel("Email").FillAsync(email);
        await page.Locator("input[type=password]").FillAsync(TestData.Password);
        await page.Locator("form button[type=submit]").ClickAsync();
        await Assertions.Expect(page.Locator("h1.greeting")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await page.GotoAsync(BaseUrl + "/tasks");
        await page.GetByRole(AriaRole.Button, new() { Name = "Новая задача" }).ClickAsync();
        await page.Locator("#task-form input").First.FillAsync("Проверка опубликованной сборки");
        await page.Locator(".modal-footer button[type=submit]").ClickAsync();
        await Assertions.Expect(page.Locator(".list-title", new() { HasText = "Проверка опубликованной сборки" })).ToBeVisibleAsync(new() { Timeout = 30_000 });

        Assert.Empty(errors);
    }

    private static async Task<string> ConfirmationLinkAsync(string email)
    {
        using var http = new HttpClient { BaseAddress = new Uri(MailpitUrl!) };
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var search = await http.GetFromJsonAsync<JsonElement>("/api/v1/search?query=" + Uri.EscapeDataString("to:" + email));
            var messages = search.GetProperty("messages");
            if (messages.GetArrayLength() > 0)
            {
                var id = messages[0].GetProperty("ID").GetString();
                var message = await http.GetFromJsonAsync<JsonElement>("/api/v1/message/" + id);
                var match = ConfirmLink().Match(message.GetProperty("Text").GetString() ?? string.Empty);
                if (match.Success)
                {
                    return match.Value;
                }
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("No confirmation email arrived in Mailpit.");
    }

    [GeneratedRegex(@"https?://\S+/confirm-email#\S+")]
    private static partial Regex ConfirmLink();
}
