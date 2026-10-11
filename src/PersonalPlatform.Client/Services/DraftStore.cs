namespace PersonalPlatform.Client.Services;

/// <summary>
/// Unsaved note/journal text kept in memory only, so navigating away or a failed save does not lose typing.
/// Nothing is written to browser storage; the store is cleared on sign-out.
/// </summary>
public sealed class DraftStore
{
    private readonly Dictionary<string, (string? Title, string Content)> _drafts = new(StringComparer.Ordinal);

    public bool TryGet(string key, out (string? Title, string Content) draft) => _drafts.TryGetValue(key, out draft);

    public void Set(string key, string? title, string content) => _drafts[key] = (title, content);

    public void Remove(string key) => _drafts.Remove(key);

    public void Clear() => _drafts.Clear();
}
