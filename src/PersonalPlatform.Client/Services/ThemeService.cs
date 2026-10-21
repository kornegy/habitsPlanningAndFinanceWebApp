using Microsoft.JSInterop;
using PersonalPlatform.Contracts.Profile;

namespace PersonalPlatform.Client.Services;

/// <summary>Switches the page between the system, light and dark colour schemes (see <c>wwwroot/js/theme.js</c>).</summary>
public sealed class ThemeService(IJSRuntime js)
{
    private IJSObjectReference? _module;

    public ThemePreference Current { get; private set; }

    public async Task ApplyAsync(ThemePreference theme)
    {
        Current = theme;
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");
        await _module.InvokeVoidAsync("applyTheme", theme switch
        {
            ThemePreference.Light => "light",
            ThemePreference.Dark => "dark",
            _ => "system",
        });
    }
}
