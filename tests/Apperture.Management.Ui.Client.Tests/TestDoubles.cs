using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>In-memory stand-in for the shell's localStorage-backed View preference JS interop.</summary>
internal sealed class FakeJsRuntime : IJSRuntime
{
    public Dictionary<string, string?> Stored { get; } = new();

    /// <summary>Runs inside a <c>getViewPreference</c> call, i.e. while a resolution pass is suspended at an await -- lets a test navigate mid-resolution.</summary>
    public Action? DuringGetViewPreference { get; set; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        switch (identifier)
        {
            case "appertureShell.getViewPreference":
            {
                DuringGetViewPreference?.Invoke();
                var userId = (string)args![0]!;
                var value = Stored.TryGetValue(userId, out var v) ? v : null;
                return ValueTask.FromResult((TValue)(object?)value!);
            }
            case "appertureShell.setViewPreference":
            {
                var userId = (string)args![0]!;
                var value = (string)args[1]!;
                Stored[userId] = value;
                return ValueTask.FromResult(default(TValue)!);
            }
            default:
                throw new InvalidOperationException($"Unexpected JS interop call in test: {identifier}");
        }
    }
}

internal sealed class TestNavigationManager : NavigationManager
{
    public List<(string Uri, bool Replace)> Navigations { get; } = new();

    /// <summary>Full-page (<c>forceLoad</c>) navigations only: a document load that discards all WASM state.</summary>
    public List<string> FullPageLoads { get; } = new();

    public TestNavigationManager(string baseUri, string uri) => Initialize(baseUri, uri);

    /// <summary>Moves the simulated address bar without raising LocationChanged, mimicking a fresh page load/new tab.</summary>
    public void SetLocationWithoutNotifying(string uri) => Uri = ToAbsoluteUri(uri).ToString();

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        Navigations.Add((uri, options.ReplaceHistoryEntry));
        if (options.ForceLoad)
            FullPageLoads.Add(uri);
        Uri = ToAbsoluteUri(uri).ToString();
        // Blazor WebAssembly raises LocationChanged synchronously from inside NavigateTo -- this is
        // exactly the reentrancy Critical #2's fix guards against.
        NotifyLocationChanged(false);
    }
}
