namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Path segment–aware matching for nav hrefs and route ownership (avoids prefix traps like /config vs /configuration).
/// </summary>
public static class NavPathMatching
{
    /// <summary>
    /// True if <paramref name="path"/> is exactly <paramref name="basePath"/> or a nested path under it (next char is '/').
    /// </summary>
    public static bool IsPathUnder(string path, string basePath)
    {
        var p = NormalizePath(path);
        var b = NormalizePath(basePath);
        if (b.Length == 0) return false;
        return p.Equals(b, StringComparison.OrdinalIgnoreCase)
            || (p.Length > b.Length && p.StartsWith(b + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True if the current path matches a menu href exactly or continues with a path segment boundary.
    /// </summary>
    public static bool PathMatchesMenuHref(string path, string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return false;
        var h = NormalizePath(href);
        if (h.Length == 0) return false;
        var p = NormalizePath(path);
        if (p.Length == 0) p = "/";
        return p.Equals(h, StringComparison.OrdinalIgnoreCase)
            || (p.Length > h.Length && p.StartsWith(h + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path) => path.TrimEnd('/');
}
