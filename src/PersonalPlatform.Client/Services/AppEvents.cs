namespace PersonalPlatform.Client.Services;

/// <summary>Notifies open pages that data changed elsewhere in the UI (e.g. a task created from the top bar).</summary>
public sealed class AppEvents
{
    public event Action? DataChanged;

    public void RaiseDataChanged() => DataChanged?.Invoke();
}
