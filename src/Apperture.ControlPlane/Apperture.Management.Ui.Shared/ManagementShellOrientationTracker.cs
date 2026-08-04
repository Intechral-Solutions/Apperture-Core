using Apperture.Management.Ui.Shared.Models;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Tracks the observed navigation URI for shell orientation and resolves route-derived breadcrumbs.
/// Observation includes path + query (so later contextual query changes can trigger reevaluation);
/// the resolver still strips query/fragment for UIX-2 trail rules.
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
        if (Uri.TryCreate(raw, UriKind.Absolute, out var absolute))
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
        var path = EntityShellTitles.NormalizePath(key);
        orientation = ManagementBreadcrumbResolver.ResolveOrientation(path);

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
        return ManagementBreadcrumbResolver.ResolveOrientation(EntityShellTitles.NormalizePath(key));
    }
}
