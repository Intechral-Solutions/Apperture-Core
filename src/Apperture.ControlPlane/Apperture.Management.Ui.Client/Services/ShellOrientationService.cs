using Apperture.Management.Ui.Shared;
using Microsoft.AspNetCore.Components;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Optional page-provided title/subtitle for the shell top bar (typically from <c>PageHeader</c>).
/// </summary>
public sealed class ShellOrientationService
{
    public string? ContextPath { get; private set; }
    public string? PageTitle { get; private set; }
    public string? PageSubtitle { get; private set; }

    /// <summary>Raised after <see cref="SetPageContext"/> updates. TopBar subscribes and re-renders outside the publisher's render pass.</summary>
    public event Action? Changed;

    public void SetPageContext(string path, string? title, string? subtitle)
    {
        var nextPath = EntityShellTitles.NormalizePath(path);
        var nextTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        var nextSubtitle = string.IsNullOrWhiteSpace(subtitle) ? null : subtitle.Trim();

        if (string.Equals(ContextPath, nextPath, StringComparison.Ordinal)
            && string.Equals(PageTitle, nextTitle, StringComparison.Ordinal)
            && string.Equals(PageSubtitle, nextSubtitle, StringComparison.Ordinal))
        {
            return;
        }

        ContextPath = nextPath;
        PageTitle = nextTitle;
        PageSubtitle = nextSubtitle;
        Changed?.Invoke();
    }

    /// <summary>
    /// Clears page overrides after a route change.
    /// Does not raise <see cref="Changed"/> — the shell location handler schedules a single re-render
    /// so route-derived orientation and cleared overrides apply together (avoids mid-navigation double render).
    /// </summary>
    public void ResetPageContext()
    {
        ContextPath = null;
        PageTitle = null;
        PageSubtitle = null;
    }

    public bool AppliesTo(string currentPath) =>
        ContextPath is not null
        && string.Equals(ContextPath, EntityShellTitles.NormalizePath(currentPath), StringComparison.OrdinalIgnoreCase);

    /// <summary>Publishes shell title for the current URI (call after async entity load).</summary>
    public void PublishForCurrentUri(NavigationManager navigation, string? title, string? subtitle)
    {
        var path = EntityShellTitles.NormalizePath(new Uri(navigation.Uri).AbsolutePath);
        SetPageContext(path, title, subtitle);
    }
}
