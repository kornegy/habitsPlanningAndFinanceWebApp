using System.Globalization;
using PersonalPlatform.Contracts.Profile;

namespace PersonalPlatform.Client.Services;

/// <summary>
/// UI language (Russian/English) and user-preference formatting. Texts are written inline as
/// <c>L["Русский", "English"]</c>, so a translation can never go missing.
/// </summary>
public sealed class Loc
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private Dictionary<string, int> _minorUnits = new(StringComparer.Ordinal);

    public Loc()
    {
        IsEnglish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "ru";
    }

    public event Action? Changed;

    public bool IsEnglish { get; private set; }

    public CultureInfo Culture => IsEnglish ? English : Russian;

    public DateFormatPreference DateFormat { get; private set; } = DateFormatPreference.DayMonthYear;

    public TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.Utc;

    public DayOfWeek FirstDayOfWeek { get; private set; } = DayOfWeek.Monday;

    public string this[string russian, string english] => IsEnglish ? english : russian;

    public void Apply(ProfileDto profile, IReadOnlyList<CurrencyInfoDto>? currencies)
    {
        IsEnglish = profile.Locale.StartsWith("en", StringComparison.Ordinal);
        DateFormat = profile.DateFormat;
        FirstDayOfWeek = profile.FirstDayOfWeek;
        Zone = TimeZoneInfo.TryFindSystemTimeZoneById(profile.TimeZoneId, out var zone) ? zone : TimeZoneInfo.Utc;
        if (currencies is not null)
        {
            _minorUnits = currencies.ToDictionary(c => c.Code, c => c.MinorUnits, StringComparer.Ordinal);
        }

        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        Changed?.Invoke();
    }

    public void SetLanguage(bool english)
    {
        IsEnglish = english;
        Changed?.Invoke();
    }

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).DateTime);

    public string Date(DateOnly date) => DateFormat switch
    {
        DateFormatPreference.MonthDayYear => date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
        DateFormatPreference.Iso => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
    };

    public string Date(DateOnly? date) => date is { } d ? Date(d) : string.Empty;

    public string DayHeading(DateOnly date) => Capitalise(date.ToString("dddd, d MMMM", Culture));

    public string ShortDay(DateOnly date) => Capitalise(date.ToString("ddd d", Culture));

    public string MonthYear(int year, int month) => Capitalise(new DateOnly(year, month, 1).ToString("MMMM yyyy", Culture));

    public string MonthShort(int month) => Capitalise(new DateOnly(2000, month, 1).ToString("MMM", Culture)).TrimEnd('.');

    public DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    public string Time(DateTimeOffset instant) => ToLocal(instant).ToString("HH:mm", CultureInfo.InvariantCulture);

    public string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public string DateTime(DateTimeOffset instant) => $"{Date(DateOnly.FromDateTime(ToLocal(instant).DateTime))} {Time(instant)}";

    public int MinorUnits(string currency) => _minorUnits.TryGetValue(currency, out var units) ? units : 2;

    public string Money(decimal amount, string currency) =>
        $"{amount.ToString("N" + MinorUnits(currency), Culture)} {currency}";

    /// <summary>"5 дней" / "5 days", with the Russian plural form chosen by the last digits.</summary>
    public string Days(int count)
    {
        if (IsEnglish)
        {
            return count == 1 ? "1 day" : $"{count.ToString(Culture)} days";
        }

        var (lastTwo, last) = (Math.Abs(count) % 100, Math.Abs(count) % 10);
        var word = lastTwo is >= 11 and <= 14 ? "дней" : last switch { 1 => "день", 2 or 3 or 4 => "дня", _ => "дней" };
        return $"{count.ToString(Culture)} {word}";
    }

    /// <summary>"2 ч 30 мин" / "2 h 30 min"; whole hours and minutes only.</summary>
    public string Duration(TimeSpan duration)
    {
        var (hours, minutes) = ((int)duration.TotalHours, duration.Minutes);
        var (h, m) = IsEnglish ? ("h", "min") : ("ч", "мин");
        return (hours, minutes) switch
        {
            (0, _) => $"{minutes} {m}",
            (_, 0) => $"{hours} {h}",
            _ => $"{hours} {h} {minutes} {m}",
        };
    }

    public string Percent(decimal value) => $"{value.ToString("0.#", Culture)}%";

    /// <summary>Converts a local date and time in the user's zone to an absolute instant (DST gaps move forward).</summary>
    public DateTimeOffset ToInstant(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        while (Zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(15);
        }

        var offset = Zone.IsAmbiguousTime(local) ? Zone.GetAmbiguousTimeOffsets(local).Max() : Zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(ToLocal(instant).DateTime);

    public DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek - (int)FirstDayOfWeek + 7) % 7));

    private string Capitalise(string text) => text.Length == 0 ? text : char.ToUpper(text[0], Culture) + text[1..];
}
