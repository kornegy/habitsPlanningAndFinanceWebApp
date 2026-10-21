using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Finance;

namespace PersonalPlatform.Domain.Users;

public enum DateFormatPreference
{
    DayMonthYear = 0, // 31.12.2026
    MonthDayYear = 1, // 12/31/2026
    Iso = 2,          // 2026-12-31
}

/// <summary>Colour scheme of the interface. <see cref="System"/> follows the operating system setting.</summary>
public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Per-user preferences. One row per user; the primary key equals the owner id.</summary>
public sealed class UserProfile : IOwnedEntity
{
    public const int DisplayNameMaxLength = 80;
    public const string DefaultTimeZone = "UTC";
    public const string DefaultLocale = "ru-RU";
    public const string DefaultCurrency = "RUB";

    public static readonly IReadOnlyList<string> SupportedLocales = ["ru-RU", "en-US"];

    private UserProfile()
    {
    }

    public UserProfile(Guid ownerId, string displayName, string timeZoneId, string locale, string currencyCode, DateTimeOffset now)
    {
        OwnerId = ownerId;
        CreatedAt = now;
        Update(displayName, timeZoneId, locale, currencyCode, DateFormatPreference.DayMonthYear, DayOfWeek.Monday, allowOverlappingTimeBlocks: true, ThemePreference.System, now);
    }

    public Guid OwnerId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>IANA time zone id (e.g. <c>Europe/Moscow</c>). Determines the user's "today".</summary>
    public string TimeZoneId { get; private set; } = DefaultTimeZone;

    public string Locale { get; private set; } = DefaultLocale;

    /// <summary>Default currency for new accounts and dashboard summaries.</summary>
    public string CurrencyCode { get; private set; } = DefaultCurrency;

    public DateFormatPreference DateFormat { get; private set; }

    public DayOfWeek FirstDayOfWeek { get; private set; } = DayOfWeek.Monday;

    /// <summary>When false, the planner rejects time blocks that overlap existing ones.</summary>
    public bool AllowOverlappingTimeBlocks { get; private set; } = true;

    public ThemePreference Theme { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public void Update(
        string displayName,
        string timeZoneId,
        string locale,
        string currencyCode,
        DateFormatPreference dateFormat,
        DayOfWeek firstDayOfWeek,
        bool allowOverlappingTimeBlocks,
        ThemePreference theme,
        DateTimeOffset now)
    {
        Rule.Require(TimeZones.IsSupported(timeZoneId), "invalid_time_zone", "Unknown time zone.");
        Rule.Require(SupportedLocales.Contains(locale), "invalid_locale", "Unsupported locale.");
        Rule.Require(Currencies.IsSupported(currencyCode), "invalid_currency", "Unsupported currency.");
        Rule.Require(Enum.IsDefined(dateFormat), "invalid_date_format", "Unknown date format.");
        Rule.Require(firstDayOfWeek is DayOfWeek.Monday or DayOfWeek.Sunday, "invalid_first_day", "Week must start on Monday or Sunday.");
        Rule.Require(Enum.IsDefined(theme), "invalid_theme", "Unknown theme.");

        DisplayName = Rule.OptionalText(displayName, DisplayNameMaxLength, "invalid_display_name", "Display name") ?? string.Empty;
        TimeZoneId = timeZoneId;
        Locale = locale;
        CurrencyCode = currencyCode;
        DateFormat = dateFormat;
        FirstDayOfWeek = firstDayOfWeek;
        AllowOverlappingTimeBlocks = allowOverlappingTimeBlocks;
        Theme = theme;
        UpdatedAt = now;
    }

    public TimeZoneInfo GetTimeZone() => TimeZones.Find(TimeZoneId);
}
