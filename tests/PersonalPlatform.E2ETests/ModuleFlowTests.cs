using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PersonalPlatform.E2ETests;

public sealed class ModuleFlowTests(BrowserEnvironment env)
{
    private const string ScriptTitle = "Отчёт <script>alert('xss')</script>";
    private const string ImagePayload = "<img src=x onerror=alert('xss')> идея";

    [Fact]
    public async Task Task_is_created_rendered_as_text_completed_and_found_by_search()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Task_is_created_rendered_as_text_completed_and_found_by_search));
        await session.SignInAsNewUserAsync();
        var page = session.Page;

        await page.GotoAsync(session.Url("tasks"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Новая задача" }).ClickAsync();
        await page.Locator("#task-form input").First.FillAsync(ScriptTitle);
        await session.SubmitModalAsync();

        // The markup is shown literally, never executed.
        var title = page.Locator(".list-title", new() { HasText = "<script>" });
        await Assertions.Expect(title).ToHaveTextAsync(ScriptTitle);

        await page.GetByRole(AriaRole.Button, new() { Name = "Выполнено: " + ScriptTitle }).ClickAsync();
        await page.GetByRole(AriaRole.Tab, new() { Name = "Завершённые" }).ClickAsync();
        await Assertions.Expect(page.Locator(".list-item.done", new() { HasText = "Отчёт" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Tab, new() { Name = "Все" }).ClickAsync();
        await page.GetByLabel("Поиск задач").FillAsync("нет-такой-задачи");
        await Assertions.Expect(page.GetByText("Задач нет")).ToBeVisibleAsync();
        await page.GetByLabel("Поиск задач").FillAsync("отч");
        await Assertions.Expect(page.Locator(".list-title", new() { HasText = "Отчёт" })).ToBeVisibleAsync();

        session.AssertHealthy();
    }

    [Fact]
    public async Task Habit_is_created_and_checked_off_for_today()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Habit_is_created_and_checked_off_for_today));
        await session.SignInAsNewUserAsync();
        var page = session.Page;

        await page.GotoAsync(session.Url("habits"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Новая привычка" }).ClickAsync();
        await page.Locator("#habit-form input").First.FillAsync("Читать 20 минут");
        await session.SubmitModalAsync();

        var check = page.GetByRole(AriaRole.Button, new() { Name = "Отметить сегодня: Читать 20 минут" });
        await Assertions.Expect(check).ToHaveAttributeAsync("aria-pressed", "false");
        await check.ClickAsync();
        await Assertions.Expect(check).ToHaveAttributeAsync("aria-pressed", "true");

        // The completion is persisted, not just client state.
        await page.ReloadAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Отметить сегодня: Читать 20 минут" })).ToHaveAttributeAsync("aria-pressed", "true");

        await page.GetByRole(AriaRole.Link, new() { Name = "Читать 20 минут" }).ClickAsync();
        await Assertions.Expect(page.Locator(".heatmap")).ToBeVisibleAsync();
        session.AssertHealthy();
    }

    [Fact]
    public async Task Habits_can_be_reset_after_confirmation()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Habits_can_be_reset_after_confirmation));
        await session.SignInAsNewUserAsync();
        var page = session.Page;

        await page.GotoAsync(session.Url("habits"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Новая привычка" }).ClickAsync();
        await page.Locator("#habit-form input").First.FillAsync("Зарядка");
        await session.SubmitModalAsync();
        var check = page.GetByRole(AriaRole.Button, new() { Name = "Отметить сегодня: Зарядка" });
        await check.ClickAsync();
        await Assertions.Expect(check).ToHaveAttributeAsync("aria-pressed", "true");

        await page.GetByRole(AriaRole.Button, new() { Name = "Начать заново" }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToContainTextAsync("Отменить это действие нельзя");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Сбросить отметки" }).ClickAsync();

        await Assertions.Expect(check).ToHaveAttributeAsync("aria-pressed", "false");
        await page.ReloadAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Отметить сегодня: Зарядка" })).ToHaveAttributeAsync("aria-pressed", "false");
        session.AssertHealthy();
    }

    [Fact]
    public async Task Bad_habit_is_tracked_and_a_relapse_restarts_the_counter()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Bad_habit_is_tracked_and_a_relapse_restarts_the_counter));
        await session.SignInAsNewUserAsync();
        var page = session.Page;

        await page.GotoAsync(session.Url("habits"));
        await page.GetByRole(AriaRole.Link, new() { Name = "Вредные: бросаю" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Бросить привычку" }).ClickAsync();
        await page.Locator("#quit-habit-form input").First.FillAsync("Сладкое");
        var quitDate = page.Locator("#quit-habit-form input[type=date]");
        var userToday = DateOnly.ParseExact((await quitDate.GetAttributeAsync("max"))!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture); // the user's local date
        await quitDate.FillAsync(userToday.AddDays(-5).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        await session.SubmitModalAsync();

        var item = page.Locator(".quit-item", new() { HasText = "Сладкое" });
        await Assertions.Expect(item).ToContainTextAsync("5 дней без привычки");

        await item.GetByRole(AriaRole.Button, new() { Name = "Срыв" }).ClickAsync();
        await page.Locator("#relapse-form textarea").FillAsync("стресс");
        await page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Отметить срыв" }).ClickAsync();
        await Assertions.Expect(item).ToContainTextAsync("0 дней без привычки");
        await Assertions.Expect(item).ToContainTextAsync("Рекорд: 5 дней");

        await item.GetByRole(AriaRole.Link, new() { Name = "Сладкое" }).ClickAsync();
        await Assertions.Expect(page.Locator(".heatmap-cell.relapse")).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByText("стресс")).ToBeVisibleAsync();
        session.AssertHealthy();
    }

    [Fact]
    public async Task Expense_and_transfer_update_account_balances_exactly()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Expense_and_transfer_update_account_balances_exactly));
        await session.SignInAsNewUserAsync();
        var page = session.Page;

        await page.GotoAsync(session.Url("finance"));
        await CreateAccountAsync(session, "Карта", "15000");
        await CreateAccountAsync(session, "Наличные", "0");

        await page.GotoAsync(session.Url("finance/transactions?new=expense"));
        var form = page.Locator("#tx-form");
        // Selects are wrapped in labels whose accessible names include the option texts, so pick them by position.
        await form.Locator("select").Nth(0).SelectOptionAsync(new SelectOptionValue { Label = "Карта (RUB)" });
        await form.Locator("input[type=number]").First.FillAsync("1234.50");
        await form.GetByLabel("Описание").FillAsync("Продукты");
        await session.SubmitModalAsync();
        await Assertions.Expect(page.GetByText("Продукты")).ToBeVisibleAsync();

        await page.GotoAsync(session.Url("finance/transactions?new=transfer"));
        await form.Locator("select").Nth(0).SelectOptionAsync(new SelectOptionValue { Label = "Карта (RUB)" });
        await form.Locator("select").Nth(1).SelectOptionAsync(new SelectOptionValue { Label = "Наличные (RUB)" });
        await form.Locator("input[type=number]").First.FillAsync("1000");
        await session.SubmitModalAsync();
        await Assertions.Expect(page.Locator(".modal-footer")).ToHaveCountAsync(0);

        await page.GotoAsync(session.Url("finance"));
        await Assertions.Expect(BalanceCell(page, "Карта")).ToHaveTextAsync(Money("12 765,50"));
        await Assertions.Expect(BalanceCell(page, "Наличные")).ToHaveTextAsync(Money("1 000,00"));
        session.AssertHealthy();
    }

    [Fact]
    public async Task Planner_block_is_created_from_a_calendar_slot()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Planner_block_is_created_from_a_calendar_slot));
        await session.SignInAsNewUserAsync();
        var page = session.Page;

        await page.GotoAsync(session.Url("planner"));
        await page.Locator(".planner-slot").Nth(40).ClickAsync();
        await page.Locator("#block-form input").First.FillAsync("Глубокая работа");
        await session.SubmitModalAsync();

        await Assertions.Expect(page.Locator(".planner-block", new() { HasText = "Глубокая работа" })).ToBeVisibleAsync();
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".planner-block", new() { HasText = "Глубокая работа" })).ToBeVisibleAsync();
        session.AssertHealthy();
    }

    [Fact]
    public async Task Note_autosaves_only_after_server_confirmation_and_is_searchable()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Note_autosaves_only_after_server_confirmation_and_is_searchable));
        await session.SignInAsNewUserAsync();
        var page = session.Page;

        await page.GotoAsync(session.Url("notes?new=1"));
        await page.Locator(".note-title-input").FillAsync("Идеи на неделю");
        await page.Locator(".note-body").FillAsync(ImagePayload);
        await Assertions.Expect(page.GetByText("Сохранено")).ToBeVisibleAsync();

        // A fresh page load shows the saved content, so "Сохранено" reflected a real server write.
        await page.GotoAsync(session.Url("notes"));
        await page.GetByLabel("Поиск заметок").FillAsync("идея");
        var item = page.Locator(".note-list-item", new() { HasText = "Идеи на неделю" });
        await Assertions.Expect(item).ToBeVisibleAsync();
        await item.ClickAsync();
        await Assertions.Expect(page.Locator(".note-body")).ToHaveValueAsync(ImagePayload);

        Assert.Equal(0, await page.Locator("img[src=x]").CountAsync());
        session.AssertHealthy();
    }

    [Fact]
    public async Task Mobile_layout_uses_bottom_navigation()
    {
        await using var session = await BrowserSession.StartAsync(env, nameof(Mobile_layout_uses_bottom_navigation), new ViewportSize { Width = 390, Height = 844 });
        await session.SignInAsNewUserAsync();
        var page = session.Page;

        await Assertions.Expect(page.Locator(".mobile-nav")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".sidebar")).ToBeHiddenAsync();
        await page.Locator(".mobile-nav").GetByRole(AriaRole.Link, new() { Name = "Задачи" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/tasks$"));

        // No horizontal scrolling at phone width.
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"));
        session.AssertHealthy();
    }

    [Fact]
    public async Task Harness_detects_script_execution()
    {
        // Guards the XSS assertions above: if dialog capture ever broke, they would pass vacuously.
        await using var session = await BrowserSession.StartAsync(env, nameof(Harness_detects_script_execution));
        await session.Page.GotoAsync(session.Url("login"));

        await session.Page.EvaluateAsync("() => alert('probe')");

        Assert.Equal(["probe"], session.Dialogs);
    }

    private static async Task CreateAccountAsync(BrowserSession session, string name, string openingBalance)
    {
        var page = session.Page;
        await page.GetByRole(AriaRole.Button, new() { Name = "Счёт", Exact = true }).ClickAsync();
        await page.Locator("#account-form input").First.FillAsync(name);
        await page.Locator("#account-form input[type=number]").FillAsync(openingBalance);
        await session.SubmitModalAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true })).ToBeVisibleAsync();
    }

    private static ILocator BalanceCell(IPage page, string account) =>
        page.Locator("tr", new() { Has = page.GetByRole(AriaRole.Link, new() { Name = account, Exact = true }) }).Locator("td.num");

    /// <summary>Russian number formatting uses no-break spaces as group and currency separators.</summary>
    private static Regex Money(string amount) =>
        new("^" + Regex.Escape(amount).Replace("\\ ", "\\s", StringComparison.Ordinal) + "\\sRUB$");
}
