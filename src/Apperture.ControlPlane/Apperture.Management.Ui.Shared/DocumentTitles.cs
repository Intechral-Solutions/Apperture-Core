namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Browser tab and history titles. Built from what the shell top bar already shows: the page title
/// (a record name on entity pages, otherwise the page heading) and the route's static label.
/// No product name. Long values are shortened so the tab stays readable.
/// </summary>
public static class DocumentTitles
{
    public const int MaxLength = 60;

    private const string Separator = " — ";
    private const string Ellipsis = "…";

    /// <summary>Route labels that start with an action. The page title already carries the whole title.</summary>
    private static readonly string[] ActionLeads =
        ["New", "Add", "Delete", "Start", "Create", "Manage", "Reset", "Set up", "Forgot", "Verify"];

    /// <summary>Static headings that mean nothing in a tab on their own.</summary>
    private static readonly string[] AmbiguousStaticTitles = ["Overview"];

    /// <summary>
    /// Composes the document title.
    /// </summary>
    /// <param name="pageTitle">The title the shell shows: a record name on entity pages, else the heading.</param>
    /// <param name="routeTitle">The route's static label ("Connection", "Runs", "User").</param>
    /// <param name="nearestParentLabel">Nearest breadcrumb parent, used only for ambiguous static headings.</param>
    /// <returns>The title, or null when nothing identifies the screen.</returns>
    public static string? Compose(string? pageTitle, string? routeTitle, string? nearestParentLabel = null)
    {
        var page = Clean(pageTitle);
        var route = Clean(routeTitle);
        var parent = Clean(nearestParentLabel);

        if (page is null)
            return route is null ? null : Shorten(route);
        if (route is null)
            return Shorten(page);

        if (Same(page, route))
        {
            if (parent is not null
                && !Same(parent, page)
                && AmbiguousStaticTitles.Any(t => Same(t, page)))
            {
                return Join(page, parent);
            }

            return Shorten(page);
        }

        // A heading that means nothing on its own is never a useful qualifier.
        if (AmbiguousStaticTitles.Any(t => Same(t, route)))
            return Shorten(page);

        // "Edit" + the record name reads as a phrase, not a type and a name.
        if (Same(route, "Edit"))
            return Shorten($"Edit {page}");

        if (LedByAction(route)
            || page.Contains(route, StringComparison.OrdinalIgnoreCase)
            || SharesFirstWord(page, route))
        {
            return Shorten(page);
        }

        // A record page or an integration panel: "<what it is> — <which one>".
        return Join(route, page);
    }

    /// <summary>
    /// <see cref="Compose"/> for a request path. Some route rules label the page with the record's own URL key
    /// (a customer's business key). That is a technical id, not a name, so the nearest parent label takes its place.
    /// </summary>
    public static string? ComposeForRoute(
        string? pageTitle,
        string? routeTitle,
        string normalizedPath,
        string? nearestParentLabel)
    {
        var route = LabelIsTheUrlKey(routeTitle, normalizedPath)
            ? nearestParentLabel
            : routeTitle;

        return Compose(pageTitle, route, nearestParentLabel);
    }

    private const string KeyProbe = "zzurlkeyprobe";

    /// <summary>
    /// True when the route rule labels the page with the URL's own last segment (a record key), as opposed to a real
    /// label that merely matches the segment (<c>/runs</c> is labelled "Runs"). Decided by asking the resolver what it
    /// would label the same route with a probe value as the last segment: a rule that echoes the segment echoes the probe.
    /// </summary>
    private static bool LabelIsTheUrlKey(string? routeTitle, string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(routeTitle)
            || !EntityShellTitles.LooksLikeRouteKey(routeTitle, normalizedPath))
        {
            return false;
        }

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            return false;

        segments[^1] = KeyProbe;
        var probed = ManagementBreadcrumbResolver.ResolveOrientation("/" + string.Join('/', segments));
        return string.Equals(probed.Title, KeyProbe, StringComparison.Ordinal);
    }

    /// <summary>Trims, collapses whitespace and returns null for blank input.</summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool Same(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool LedByAction(string route) =>
        ActionLeads.Any(lead =>
            route.StartsWith(lead, StringComparison.OrdinalIgnoreCase)
            && (route.Length == lead.Length || route[lead.Length] == ' '));

    private static bool SharesFirstWord(string a, string b)
    {
        static string First(string s)
        {
            var word = s.Split(' ', 2)[0].TrimEnd('s', 'S');
            return word;
        }

        var fa = First(a);
        return fa.Length > 0 && string.Equals(fa, First(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "<paramref name="lead"/> — <paramref name="name"/>", shortening the name first so the lead survives.
    /// </summary>
    private static string Join(string lead, string name)
    {
        var budget = MaxLength - lead.Length - Separator.Length;
        if (budget < 8)
            return Shorten($"{lead}{Separator}{name}");

        return $"{lead}{Separator}{Shorten(name, budget)}";
    }

    private static string Shorten(string value) => Shorten(value, MaxLength);

    private static string Shorten(string value, int max)
    {
        if (value.Length <= max)
            return value;

        var cut = max - Ellipsis.Length;

        // Never end on half of a surrogate pair (an emoji or other character outside the BMP),
        // which would leave a malformed character in the tab title.
        if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
            cut--;

        return value[..cut].TrimEnd() + Ellipsis;
    }
}
