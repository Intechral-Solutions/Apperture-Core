namespace Apperture.Management.Ui.Shared;

public enum ResourceContextViewMode
{
    Scoped,
    Partial,
    Detail,
    Unscoped
}

public static class ResourceContextViewRoutes
{
    private static readonly string[] ScopedListPaths = ["/integrations", "/connections", "/schedules", "/runs"];
    private static readonly string[] PartialPaths = ["/control/center"];

    public static ResourceContextViewMode Resolve(string path)
    {
        var normalized = Normalize(path);

        foreach (var scoped in ScopedListPaths)
        {
            if (string.Equals(normalized, scoped, StringComparison.OrdinalIgnoreCase))
                return ResourceContextViewMode.Scoped;
        }

        foreach (var partial in PartialPaths)
        {
            if (string.Equals(normalized, partial, StringComparison.OrdinalIgnoreCase))
                return ResourceContextViewMode.Partial;
        }

        if (IsDetailRoute(normalized))
            return ResourceContextViewMode.Detail;

        return ResourceContextViewMode.Unscoped;
    }

    private static bool IsDetailRoute(string normalized)
    {
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return false;

        var root = "/" + segments[0];
        var isKnownFamily = root is "/integrations" or "/connections" or "/schedules" or "/runs";
        if (!isKnownFamily)
            return false;

        // A second segment that is not a known static route ("new") is treated as an id.
        return !string.Equals(segments[1], "new", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        var withoutQuery = path.Split('?', 2)[0].Split('#', 2)[0];
        var trimmed = withoutQuery.TrimEnd('/');
        return string.IsNullOrEmpty(trimmed) ? "/" : trimmed;
    }
}
