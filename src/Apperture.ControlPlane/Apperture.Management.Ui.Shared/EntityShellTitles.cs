namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Shell top-bar titles for entity detail pages. Never surfaces raw route keys as the primary title.
/// </summary>
public static class EntityShellTitles
{
    /// <summary>
    /// Preferred display name for the shell title, or a stable section label while the entity is loading.
    /// </summary>
    public static string Detail(string? displayName, string sectionLabel) =>
        string.IsNullOrWhiteSpace(displayName) ? sectionLabel : displayName.Trim();

    /// <summary>
    /// Normalizes a request path for shell context matching (trim, drop trailing slash).
    /// </summary>
    public static string NormalizePath(string path)
    {
        var p = path.Trim();
        if (p.Length == 0)
            return "/";
        p = p.TrimEnd('/');
        return p.Length == 0 ? "/" : p;
    }

    /// <summary>
    /// True when <paramref name="value"/> is the terminal URL segment (route key), not a human display name.
    /// </summary>
    public static bool LooksLikeRouteKey(string? value, string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var segment = normalizedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        return segment is not null
               && string.Equals(value.Trim(), segment, StringComparison.OrdinalIgnoreCase);
    }
}
