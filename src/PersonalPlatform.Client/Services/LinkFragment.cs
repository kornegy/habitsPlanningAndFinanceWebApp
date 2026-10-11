namespace PersonalPlatform.Client.Services;

/// <summary>Parses "#userId=...&amp;token=..." from email links.</summary>
public static class LinkFragment
{
    public static (Guid UserId, string Token)? Parse(string uri)
    {
        var hash = uri.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0)
        {
            return null;
        }

        var values = uri[(hash + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0], kv => Uri.UnescapeDataString(kv[1]), StringComparer.Ordinal);
        return values.TryGetValue("userId", out var id) && Guid.TryParse(id, out var userId)
            && values.TryGetValue("token", out var token) && token.Length is > 0 and <= 2048
            ? (userId, token)
            : null;
    }
}
