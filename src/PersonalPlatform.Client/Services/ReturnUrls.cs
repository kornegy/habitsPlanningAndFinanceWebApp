namespace PersonalPlatform.Client.Services;

/// <summary>Guards post-login redirects against open-redirect tricks (CWE-601).</summary>
public static class ReturnUrls
{
    private const int MaxLength = 512;

    /// <summary>
    /// Returns a base-relative path (without the leading slash) for a same-app return URL, or an empty string.
    /// Only "/path" forms are accepted: protocol-relative ("//host"), backslash variants, absolute URLs and
    /// control characters (which browsers silently strip, turning "/\t/host" into "//host") are rejected.
    /// </summary>
    public static string Sanitize(string? returnUrl)
    {
        if (returnUrl is not { Length: > 1 and <= MaxLength } url || url[0] != '/' || url[1] == '/')
        {
            return string.Empty;
        }

        foreach (var ch in url)
        {
            if (char.IsControl(ch) || char.IsWhiteSpace(ch) || ch == '\\')
            {
                return string.Empty;
            }
        }

        // The leading slash is removed for NavigationManager, so a colon in the path would turn "/javascript:x"
        // into an absolute "javascript:x" URI, and "C|" is read as a DOS drive by System.Uri. Both are allowed
        // only in the query string or fragment.
        var pathEnd = url.IndexOfAny(['?', '#']);
        var path = pathEnd < 0 ? url : url[..pathEnd];
        return path.AsSpan().ContainsAny(':', '|') ? string.Empty : url[1..];
    }
}
