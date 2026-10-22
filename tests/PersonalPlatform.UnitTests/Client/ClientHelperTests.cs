using System.Globalization;
using CsCheck;
using PersonalPlatform.Client.Services;
using PersonalPlatform.Contracts.Profile;

namespace PersonalPlatform.UnitTests.Client;

public sealed class ReturnUrlTests
{
    [Theory]
    [InlineData("/tasks", "tasks")]
    [InlineData("/finance/transactions?from=2026-01-01", "finance/transactions?from=2026-01-01")]
    [InlineData("/notes#id=a:b", "notes#id=a:b")]
    [InlineData("//evil.example.test", "")]
    [InlineData("/\\evil.example.test", "")]
    [InlineData("/\t/evil.example.test", "")]
    [InlineData("/\n/evil.example.test", "")]
    [InlineData("/javascript:alert(1)", "")]
    [InlineData("/data:text/html,x", "")]
    [InlineData("/C|/windows", "")]
    [InlineData("https://evil.example.test/", "")]
    [InlineData("javascript:alert(1)", "")]
    [InlineData("/", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Only_same_app_paths_are_allowed(string? input, string expected) =>
        Assert.Equal(expected, ReturnUrls.Sanitize(input));

    [Fact]
    public void Property_sanitized_urls_never_leave_the_app()
    {
        Gen.String[0, 60].Sample(raw =>
        {
            var result = ReturnUrls.Sanitize("/" + raw);
            if (result.Length == 0)
            {
                return;
            }

            var resolved = new Uri(new Uri("https://app.example.test/"), result);
            Assert.Equal("https", resolved.Scheme);
            Assert.Equal("app.example.test", resolved.Host);
        }, iter: 2000);
    }
}

public sealed class LinkFragmentTests
{
    private static readonly Guid User = Guid.Parse("0195f3a0-0000-7000-8000-0000000000aa");

    [Fact]
    public void Parses_user_and_unescaped_token_from_the_fragment()
    {
        var parsed = LinkFragment.Parse($"https://app.example.test/confirm-email#userId={User}&token=abc%2Bdef%3D%3D");

        Assert.Equal((User, "abc+def=="), parsed);
    }

    [Theory]
    [InlineData("https://app.example.test/confirm-email")]
    [InlineData("https://app.example.test/confirm-email#userId=not-a-guid&token=x")]
    [InlineData("https://app.example.test/confirm-email#userId=0195f3a0-0000-7000-8000-0000000000aa")]
    [InlineData("https://app.example.test/confirm-email#userId=0195f3a0-0000-7000-8000-0000000000aa&token=")]
    public void Incomplete_or_malformed_links_are_rejected(string uri) => Assert.Null(LinkFragment.Parse(uri));

    [Fact]
    public void Overlong_tokens_are_rejected() =>
        Assert.Null(LinkFragment.Parse($"https://app.example.test/x#userId={User}&token={new string('a', 2049)}"));
}

/// <summary>Loc.Apply sets the default thread cultures, so these tests must not run in parallel with others.</summary>
[CollectionDefinition(nameof(CultureSensitive), DisableParallelization = true)]
public sealed class CultureSensitive;

[Collection(nameof(CultureSensitive))]
public sealed class LocTests
{
    [Fact]
    public void Money_uses_currency_minor_units_and_dates_follow_the_preference()
    {
        var (culture, uiCulture) = (CultureInfo.DefaultThreadCurrentCulture, CultureInfo.DefaultThreadCurrentUICulture);
        try
        {
            var loc = new Loc();
            loc.Apply(Profile("en-US", DateFormatPreference.Iso, DayOfWeek.Sunday, "America/New_York"), [new("JPY", 0), new("KWD", 3)]);

            Assert.Equal("1,235\u00A0JPY", loc.Money(1234.5m, "JPY")); // display rounding only; stored values are exact
            Assert.Equal("1.500\u00A0KWD", loc.Money(1.5m, "KWD"));
            Assert.Equal("2026-03-08", loc.Date(new DateOnly(2026, 3, 8)));
            Assert.Equal(new DateOnly(2026, 3, 8), loc.StartOfWeek(new DateOnly(2026, 3, 11)));
            // 02:30 on the US spring-forward day does not exist; it moves to 03:00 EDT (07:00Z).
            Assert.Equal(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), loc.ToInstant(new DateOnly(2026, 3, 8), new TimeOnly(2, 30)));

            loc.Apply(Profile("ru-RU", DateFormatPreference.DayMonthYear, DayOfWeek.Monday, "Unknown/Zone"), null);
            Assert.Equal("08.03.2026", loc.Date(new DateOnly(2026, 3, 8)));
            Assert.Equal(new DateOnly(2026, 3, 9), loc.StartOfWeek(new DateOnly(2026, 3, 11)));
            Assert.Equal(TimeZoneInfo.Utc, loc.Zone);
            Assert.Equal("Привет", loc["Привет", "Hello"]);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        }
    }

    [Theory]
    [InlineData(1, "1 день")]
    [InlineData(3, "3 дня")]
    [InlineData(5, "5 дней")]
    [InlineData(11, "11 дней")]
    [InlineData(12, "12 дней")]
    [InlineData(21, "21 день")]
    [InlineData(104, "104 дня")]
    [InlineData(0, "0 дней")]
    public void Russian_day_counts_use_the_correct_plural_form(int count, string expected)
    {
        var loc = new Loc();
        loc.SetLanguage(english: false);
        Assert.Equal(expected, loc.Days(count));
    }

    [Fact]
    public void English_day_counts_use_singular_only_for_one()
    {
        var (culture, uiCulture) = (CultureInfo.DefaultThreadCurrentCulture, CultureInfo.DefaultThreadCurrentUICulture);
        try
        {
            var loc = new Loc();
            loc.SetLanguage(english: true);
            Assert.Equal("1 day", loc.Days(1));
            Assert.Equal("2 days", loc.Days(2));
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        }
    }

    private static ProfileDto Profile(string locale, DateFormatPreference format, DayOfWeek firstDay, string zone) =>
        new("user@example.test", "Test", zone, locale, "RUB", format, firstDay, false, ThemePreference.System, new DateOnly(2026, 3, 11), 1);
}

public sealed class BlockTimingTests
{
    private static readonly DateOnly Friday = new(2026, 10, 23);

    [Theory]
    [InlineData("09:00", "11:30", 0, 150, false, false)]
    [InlineData("22:00", "00:00", 1, 120, true, false)] // until midnight: the block stays on its own day
    [InlineData("22:00", "02:00", 1, 240, false, true)]
    [InlineData("22:00", "12:00", 1, 840, false, true)] // the reported case: noon is the next day, 14 hours later
    [InlineData("08:00", "08:00", 1, 1440, false, true)]
    public void End_time_not_after_the_start_ends_after_midnight(string start, string end, int endDayOffset, int minutes, bool midnight, bool nextDay)
    {
        var timing = BlockTiming.Of(Friday, TimeOnly.Parse(start, CultureInfo.InvariantCulture), TimeOnly.Parse(end, CultureInfo.InvariantCulture));

        Assert.Equal(new BlockTiming(Friday.AddDays(endDayOffset), TimeSpan.FromMinutes(minutes), midnight, nextDay), timing);
    }

    [Fact]
    public void Durations_are_written_in_hours_and_minutes()
    {
        var loc = new Loc();
        loc.SetLanguage(english: false);
        Assert.Equal("2 ч", loc.Duration(TimeSpan.FromHours(2)));
        Assert.Equal("45 мин", loc.Duration(TimeSpan.FromMinutes(45)));
        Assert.Equal("14 ч 30 мин", loc.Duration(TimeSpan.FromMinutes(870)));
        loc.SetLanguage(english: true);
        Assert.Equal("1 h 5 min", loc.Duration(TimeSpan.FromMinutes(65)));
    }
}
