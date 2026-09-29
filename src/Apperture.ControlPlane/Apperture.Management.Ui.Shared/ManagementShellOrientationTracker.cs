using Apperture.Management.Ui.Shared.Models;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Tracks the observed navigation URI for shell orientation and resolves breadcrumbs.
/// Observation includes path + query so contextual query changes reevaluate (UIX-2/4).
/// Path drives destination-local rules; query supplies typed navigation context only.
/// </summary>
public sealed class ManagementShellOrientationTracker
{
    private string? _observedPathAndQuery;

    /// <summary>Last path+query observation key, or null before the first apply.</summary>
    public string? ObservedPathAndQuery => _observedPathAndQuery;

    /// <summary>
    /// Builds the observation key from a navigation URI (absolute or relative).
    /// Includes query string; excludes fragment.
    /// </summary>
    public static string GetObservationKey(string navigationUri)
    {
        if (string.IsNullOrWhiteSpace(navigationUri))
            return "/";

        var raw = navigationUri.Trim();
        // Rooted app paths are not absolute. On Unix they parse as file URIs and encode '?'/'#' into the path.
        if (AppNavigationUri.TryGetHttpAbsolute(raw, out var absolute))
            return string.IsNullOrEmpty(absolute.Query)
                ? absolute.AbsolutePath
                : absolute.AbsolutePath + absolute.Query;

        var fragment = raw.IndexOf('#', StringComparison.Ordinal);
        if (fragment >= 0)
            raw = raw[..fragment];

        if (!raw.StartsWith('/'))
            raw = "/" + raw;

        return raw.Length == 0 ? "/" : raw;
    }

    /// <summary>
    /// Applies a navigation URI. Returns <c>false</c> when the observation key is unchanged
    /// (caller should skip re-render). Always refreshes <paramref name="orientation"/> from the resolver.
    /// </summary>
    public bool TryApply(string navigationUri, out ManagementShellOrientation orientation)
    {
        var key = GetObservationKey(navigationUri);
        orientation = ManagementBreadcrumbResolver.ResolveOrientationFromNavigationUri(navigationUri);

        if (string.Equals(key, _observedPathAndQuery, StringComparison.Ordinal))
            return false;

        _observedPathAndQuery = key;
        return true;
    }

    /// <summary>Forces observation + resolve (initial load or title-override refresh).</summary>
    public ManagementShellOrientation ForceApply(string navigationUri)
    {
        var key = GetObservationKey(navigationUri);
        _observedPathAndQuery = key;
        return ManagementBreadcrumbResolver.ResolveOrientationFromNavigationUri(navigationUri);
    }
}
