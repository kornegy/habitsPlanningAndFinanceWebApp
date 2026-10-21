namespace PersonalPlatform.Contracts.Profile;

public enum DateFormatPreference
{
    DayMonthYear = 0,
    MonthDayYear = 1,
    Iso = 2,
}

/// <summary>Colour scheme of the interface. <see cref="System"/> follows the operating system setting.</summary>
public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2,
}

public sealed record ProfileDto(
    string Email,
    string DisplayName,
    string TimeZoneId,
    string Locale,
    string CurrencyCode,
    DateFormatPreference DateFormat,
    DayOfWeek FirstDayOfWeek,
    bool AllowOverlappingTimeBlocks,
    ThemePreference Theme,
    DateOnly Today,
    uint Version);

public sealed record UpdateProfileRequest(
    string? DisplayName,
    string TimeZoneId,
    string Locale,
    string CurrencyCode,
    DateFormatPreference DateFormat,
    DayOfWeek FirstDayOfWeek,
    bool AllowOverlappingTimeBlocks,
    ThemePreference Theme,
    uint Version);

public sealed record ReferenceDataDto(IReadOnlyList<CurrencyInfoDto> Currencies, IReadOnlyList<string> TimeZones, IReadOnlyList<string> Locales);

public sealed record CurrencyInfoDto(string Code, int MinorUnits);
