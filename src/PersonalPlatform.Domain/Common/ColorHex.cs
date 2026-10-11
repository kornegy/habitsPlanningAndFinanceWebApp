namespace PersonalPlatform.Domain.Common;

/// <summary>Validates user-chosen colour markers. Only the strict <c>#RRGGBB</c> form is accepted.</summary>
public static class ColorHex
{
    public const int Length = 7;
    public const string Default = "#8A8D86";

    public static bool IsValid(string? value) =>
        value is { Length: Length } && value[0] == '#' && value.AsSpan(1).ContainsAnyExcept(HexDigits) is false;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        Rule.Require(IsValid(value), "invalid_color", "Color must be in #RRGGBB format.");
        return value.ToUpperInvariant();
    }

    private static readonly System.Buffers.SearchValues<char> HexDigits =
        System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");
}
