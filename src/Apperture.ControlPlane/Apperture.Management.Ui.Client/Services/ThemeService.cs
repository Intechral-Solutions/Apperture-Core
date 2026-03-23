using Apperture.Management.Ui.Shared;
using Microsoft.JSInterop;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>Applies Light/Dark/System theme via <c>html.dark</c> and <see cref="ThemePreferenceParser.StorageKey"/>.</summary>
public sealed class ThemeService : IDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<ThemeService>? _dotNetRef;
    private bool _initialized;

    public ThemeService(IJSRuntime js) => _js = js;

    public ThemePreference Current { get; private set; } = ThemePreference.System;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;
        _initialized = true;

        var raw = await _js.InvokeAsync<string>("appertureShell.getPreference").ConfigureAwait(false);
        Current = ThemePreferenceParser.ParseOrDefault(raw);
        await _js.InvokeVoidAsync("appertureShell.apply", ThemePreferenceParser.ToStorageValue(Current)).ConfigureAwait(false);

        _dotNetRef = DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("appertureShell.subscribeSystemTheme", _dotNetRef).ConfigureAwait(false);
    }

    public async Task SetPreferenceAsync(ThemePreference preference)
    {
        Current = preference;
        var storage = ThemePreferenceParser.ToStorageValue(preference);
        await _js.InvokeVoidAsync("appertureShell.setPreference", storage).ConfigureAwait(false);
        await _js.InvokeVoidAsync("appertureShell.apply", storage).ConfigureAwait(false);
        Changed?.Invoke();
    }

    [JSInvokable]
    public async Task OnSystemThemeChanged()
    {
        if (Current != ThemePreference.System)
            return;
        await _js.InvokeVoidAsync("appertureShell.apply", ThemePreferenceParser.ToStorageValue(ThemePreference.System))
            .ConfigureAwait(false);
        Changed?.Invoke();
    }

    public void Dispose() => _dotNetRef?.Dispose();
}
