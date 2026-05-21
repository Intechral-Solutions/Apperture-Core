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
        ContextPath = EntityShellTitles.NormalizePath(path);
        PageTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        PageSubtitle = string.IsNullOrWhiteSpace(subtitle) ? null : subtitle.Trim();
        Changed?.Invoke();
    }

    /// <summary>Clears page overrides and notifies listeners (e.g. on route change).</summary>
    public void ResetPageContext()
    {
        ContextPath = null;
        PageTitle = null;
        PageSubtitle = null;
        Changed?.Invoke();
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
