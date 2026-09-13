using Apperture.Management.Ui.Shared.Models;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Location-aware shell orientation state for the top-bar trail (UIX-2 corrective).
/// Owns re-resolution on URI changes without requiring a Blazor <c>@key</c> remount.
/// Does not subscribe to <c>NavigationManager</c> itself — the host wires location events.
/// </summary>
public sealed class ManagementBreadcrumbLocationState : IDisposable
{
    private readonly ManagementShellOrientationTracker _tracker = new();
    private bool _disposed;

    public ManagementShellOrientation Orientation { get; private set; } = ManagementShellOrientation.Empty;

    public IReadOnlyList<BreadcrumbItem> Parents => Orientation.Parents;

    public string CurrentPath { get; private set; } = "/";

    public string? ObservedPathAndQuery => _tracker.ObservedPathAndQuery;

    public bool IsDisposed => _disposed;

    /// <summary>Initial resolve from the current navigation URI.</summary>
    public void Initialize(string navigationUri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ApplyCore(navigationUri, force: true);
    }

    /// <summary>
    /// Re-resolves when the observed path+query changes.
    /// Returns <c>true</c> when state changed and the host should re-render.
    /// Returns <c>false</c> when disposed or the observation key is unchanged.
    /// </summary>
    public bool TryApplyLocation(string navigationUri)
    {
        if (_disposed)
            return false;

        return ApplyCore(navigationUri, force: false);
    }

    /// <summary>
    /// Re-resolves from the current URI even when the observation key is unchanged
    /// (e.g. page title publish after async load). Always updates <see cref="Orientation"/>.
    /// Returns <c>false</c> when disposed.
    /// </summary>
    public bool Refresh(string navigationUri)
    {
        if (_disposed)
            return false;

        ApplyCore(navigationUri, force: true);
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private bool ApplyCore(string navigationUri, bool force)
    {
        if (force)
        {
            Orientation = _tracker.ForceApply(navigationUri);
            CurrentPath = EntityShellTitles.NormalizePath(_tracker.ObservedPathAndQuery ?? "/");
            return true;
        }

        var changed = _tracker.TryApply(navigationUri, out var orientation);
        Orientation = orientation;
        CurrentPath = EntityShellTitles.NormalizePath(_tracker.ObservedPathAndQuery ?? "/");
        return changed;
    }
}
