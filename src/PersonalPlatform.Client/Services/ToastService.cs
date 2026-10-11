namespace PersonalPlatform.Client.Services;

public enum ToastKind
{
    Info,
    Success,
    Error,
}

public sealed record Toast(Guid Id, ToastKind Kind, string Message);

/// <summary>Short, non-blocking notifications announced to assistive technology via an aria-live region.</summary>
public sealed class ToastService
{
    private readonly List<Toast> _toasts = [];

    public event Action? Changed;

    public IReadOnlyList<Toast> Items => _toasts;

    public void Success(string message) => Show(ToastKind.Success, message);

    public void Error(string message) => Show(ToastKind.Error, message);

    public void Info(string message) => Show(ToastKind.Info, message);

    public void Dismiss(Guid id)
    {
        _toasts.RemoveAll(t => t.Id == id);
        Changed?.Invoke();
    }

    private void Show(ToastKind kind, string message)
    {
        var toast = new Toast(Guid.NewGuid(), kind, message);
        _toasts.Add(toast);
        if (_toasts.Count > 4)
        {
            _toasts.RemoveAt(0);
        }

        Changed?.Invoke();
        _ = DismissLaterAsync(toast.Id, kind == ToastKind.Error ? 8000 : 4000);
    }

    private async Task DismissLaterAsync(Guid id, int milliseconds)
    {
        await Task.Delay(milliseconds);
        Dismiss(id);
    }
}
