using System.Collections.Frozen;

namespace PersonalPlatform.Domain.Finance;

/// <summary>
/// Supported ISO 4217 currencies with their number of minor units (decimal places).
/// </summary>
/// <remarks>
/// Money rules of the application:
/// <list type="bullet">
/// <item>Amounts are <see cref="decimal"/> in C# and <c>numeric(19,4)</c> in PostgreSQL; floating point is never used.</item>
/// <item>Input amounts must already be expressed in the currency's minor units (e.g. at most 2 decimals for EUR,
/// 0 for JPY). Input is rejected rather than silently rounded, so stored values are exactly what the user entered.</item>
/// <item>Sums and differences of amounts in the same currency are exact (decimal addition, no rounding).</item>
/// <item>Derived ratios (budget usage percentages) are rounded with <see cref="MidpointRounding.AwayFromZero"/>.</item>
/// <item>Amounts in different currencies are never added together; reports group by currency.</item>
/// </list>
/// </remarks>
public static class Currencies
{
    public const int CodeLength = 3;

    /// <summary>Largest absolute amount accepted for a single operation or balance input.</summary>
    public const decimal MaxAbsoluteAmount = 1_000_000_000_000m;

    private static readonly FrozenDictionary<string, int> MinorUnitsByCode = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["AED"] = 2,
        ["ARS"] = 2,
        ["AUD"] = 2,
        ["AMD"] = 2,
        ["AZN"] = 2,
        ["BGN"] = 2,
        ["BRL"] = 2,
        ["BYN"] = 2,
        ["CAD"] = 2,
        ["CHF"] = 2,
        ["CLP"] = 0,
        ["CNY"] = 2,
        ["CZK"] = 2,
        ["DKK"] = 2,
        ["EUR"] = 2,
        ["GBP"] = 2,
        ["GEL"] = 2,
        ["HKD"] = 2,
        ["HUF"] = 2,
        ["IDR"] = 2,
        ["ILS"] = 2,
        ["INR"] = 2,
        ["ISK"] = 0,
        ["JPY"] = 0,
        ["KGS"] = 2,
        ["KRW"] = 0,
        ["KWD"] = 3,
        ["KZT"] = 2,
        ["MDL"] = 2,
        ["MXN"] = 2,
        ["NOK"] = 2,
        ["NZD"] = 2,
        ["PLN"] = 2,
        ["RON"] = 2,
        ["RSD"] = 2,
        ["RUB"] = 2,
        ["SAR"] = 2,
        ["SEK"] = 2,
        ["SGD"] = 2,
        ["THB"] = 2,
        ["TJS"] = 2,
        ["TRY"] = 2,
        ["UAH"] = 2,
        ["USD"] = 2,
        ["UZS"] = 2,
        ["VND"] = 0,
        ["ZAR"] = 2,
        ["BHD"] = 3,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IEnumerable<string> SupportedCodes => MinorUnitsByCode.Keys.Order(StringComparer.Ordinal);

    public static bool IsSupported(string? code) => code is { Length: CodeLength } && MinorUnitsByCode.ContainsKey(code);

    public static int MinorUnits(string code) =>
        MinorUnitsByCode.TryGetValue(code, out var units)
            ? units
            : throw new ArgumentException("Unsupported currency.", nameof(code));

    /// <summary>True when the amount has no more decimal places than the currency allows.</summary>
    public static bool HasValidPrecision(decimal amount, string code) =>
        decimal.Round(amount, MinorUnits(code), MidpointRounding.ToEven) == amount;

    public static bool IsWithinRange(decimal amount) => Math.Abs(amount) <= MaxAbsoluteAmount;
}
