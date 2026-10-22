using Microsoft.Playwright;

namespace PersonalPlatform.E2ETests;

/// <summary>Interface behaviour: popover dismissal, live language switch, colour theme and wide screens.</summary>
public sealed class InterfaceTests(BrowserEnvironment env)
{
    [Fact]
    public async Task Popovers_close_on_outside_click_and_escape()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Popovers_close_on_outside_click_and_escape));
        await session.SignInAsNewUserAsync();
        var page = session.Page;
        var topbar = page.Locator(".topbar");

        await topbar.GetByRole(AriaRole.Button, new() { Name = "Добавить" }).ClickAsync();
        await Assertions.Expect(page.Locator(".popover-menu")).ToBeVisibleAsync();
        await page.Mouse.ClickAsync(640, 600);
        await Assertions.Expect(page.Locator(".popover")).ToHaveCountAsync(0);

        await topbar.GetByRole(AriaRole.Button, new() { Name = "Напоминания" }).ClickAsync();
        await Assertions.Expect(page.Locator(".popover")).ToHaveCountAsync(1);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".popover")).ToHaveCountAsync(0);

        session.AssertHealthy();
    }

    [Fact]
    public async Task Colour_palette_closes_on_outside_click_without_closing_the_dialog()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Colour_palette_closes_on_outside_click_without_closing_the_dialog));
        await session.SignInAsNewUserAsync();
        var page = session.Page;

        await page.GotoAsync(session.Url("habits"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Новая привычка" }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        var toggle = dialog.Locator(".color-toggle");

        await toggle.ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "#5E8C61" }).ClickAsync();
        await Assertions.Expect(page.Locator(".color-panel")).ToHaveCountAsync(0);
        await Assertions.Expect(toggle).ToContainTextAsync("#5E8C61");

        await toggle.ClickAsync();
        await Assertions.Expect(page.Locator(".color-panel")).ToBeVisibleAsync();
        await page.Mouse.ClickAsync(5, 5); // outside both the palette and the dialog
        await Assertions.Expect(page.Locator(".color-panel")).ToHaveCountAsync(0);
        await Assertions.Expect(dialog).ToBeVisibleAsync();

        await toggle.ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".color-panel")).ToHaveCountAsync(0);
        await Assertions.Expect(dialog).ToBeVisibleAsync();

        session.AssertHealthy();
    }

    [Fact]
    public async Task Language_change_updates_navigation_without_reload()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Language_change_updates_navigation_without_reload));
        await session.SignInAsNewUserAsync();
        var page = session.Page;

        await page.GotoAsync(session.Url("settings"));
        await page.GetByLabel("Язык").SelectOptionAsync("en-US");
        await page.GetByRole(AriaRole.Button, new() { Name = "Сохранить" }).ClickAsync();

        var sidebar = page.Locator(".sidebar");
        await Assertions.Expect(sidebar.GetByRole(AriaRole.Link, new() { Name = "Tasks" })).ToBeVisibleAsync();
        await Assertions.Expect(sidebar.GetByRole(AriaRole.Link, new() { Name = "Задачи" })).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".topbar").GetByRole(AriaRole.Button, new() { Name = "Add" })).ToBeVisibleAsync();

        session.AssertHealthy();
    }

    [Fact]
    public async Task Dark_theme_is_saved_in_the_profile_and_restored_after_reload()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Dark_theme_is_saved_in_the_profile_and_restored_after_reload));
        await session.SignInAsNewUserAsync();
        var page = session.Page;
        var html = page.Locator("html");

        await page.GotoAsync(session.Url("settings"));
        await page.GetByLabel("Тема оформления").SelectOptionAsync("Dark");
        await Assertions.Expect(html).ToHaveAttributeAsync("data-theme", "dark");
        await page.GetByRole(AriaRole.Button, new() { Name = "Сохранить" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Настройки сохранены")).ToBeVisibleAsync();

        await page.ReloadAsync();
        await Assertions.Expect(html).ToHaveAttributeAsync("data-theme", "dark");
        Assert.Equal("rgb(22, 23, 21)", await page.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor"));

        session.AssertHealthy();
    }

    [Fact]
    public async Task Empty_note_editor_is_centred_and_journal_search_keeps_its_height()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Empty_note_editor_is_centred_and_journal_search_keeps_its_height), new ViewportSize { Width = 1920, Height = 1080 });
        await session.SignInAsNewUserAsync();
        var page = session.Page;

        await page.GotoAsync(session.Url("notes"));
        var placeholder = page.Locator(".pane-main .empty");
        await Assertions.Expect(placeholder.GetByRole(AriaRole.Button, new() { Name = "Новая заметка" })).ToBeVisibleAsync();
        var pane = (await page.Locator(".pane-main").BoundingBoxAsync())!;
        var empty = (await placeholder.BoundingBoxAsync())!;
        Assert.InRange(empty.X + (empty.Width / 2) - (pane.X + (pane.Width / 2)), -2, 2);
        Assert.InRange(empty.Y + (empty.Height / 2) - (pane.Y + (pane.Height / 2)), -40, 40);
        session.AssertHealthy();

        // A day without an entry is answered with 404 (shown as an empty entry), which the browser logs as a failed
        // request, so the console is not checked after this point.
        await page.GotoAsync(session.Url("journal"));
        var search = page.Locator(".pane-list input[type=search]");
        await Assertions.Expect(search).ToBeVisibleAsync();
        Assert.True((await search.BoundingBoxAsync())!.Height < 60);
    }

    [Fact]
    public async Task Pages_use_the_full_width_of_large_monitors()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Pages_use_the_full_width_of_large_monitors), new ViewportSize { Width = 2560, Height = 1440 });
        await session.SignInAsNewUserAsync();

        var width = await session.Page.EvaluateAsync<double>("() => document.querySelector('.page').getBoundingClientRect().width");

        Assert.True(width > 2000, $"Page content is only {width}px wide on a 2560px screen.");
        Assert.True(await session.Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"));
    }
}
