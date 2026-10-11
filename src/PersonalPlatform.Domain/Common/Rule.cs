namespace PersonalPlatform.Domain.Common;

/// <summary>Small guard helpers for domain invariants.</summary>
public static class Rule
{
    public static void Require(bool condition, string code, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(code, message);
        }
    }

    public static string RequireText(string? value, int maxLength, string code, string fieldName)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        Require(trimmed.Length > 0, code, $"{fieldName} is required.");
        Require(trimmed.Length <= maxLength, code, $"{fieldName} must be at most {maxLength} characters.");
        return trimmed;
    }

    public static string? OptionalText(string? value, int maxLength, string code, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        Require(trimmed.Length <= maxLength, code, $"{fieldName} must be at most {maxLength} characters.");
        return trimmed;
    }
}
