using Apperture.Management.Ui.Shared.Models;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Resolves the current route path to the owning section/domain.
/// Used by the shell to determine active rail state and control panel content.
/// </summary>
public static class RouteToSectionResolver
{
    /// <summary>
    /// Resolves the given path to the owning section domain name.
    /// </summary>
    /// <param name="path">The request path (e.g. from Uri.AbsolutePath), normalized (trimmed).</param>
    /// <param name="getDomain">Function to get a domain menu by name.</param>
    /// <param name="getAllDomainNames">All domain names to consider for prefix matching.</param>
    /// <returns>The domain name that owns the route, or empty string if none.</returns>
    public static string Resolve(
        string path,
        Func<string, DomainMenu?> getDomain,
        IEnumerable<string> getAllDomainNames)
    {
        var p = path.TrimEnd('/');
        if (p.Length == 0) p = "/";

        // Explicit section ownership (segment-safe; order matters)
        if (NavPathMatching.IsPathUnder(p, "/config"))
            return SectionOwnerConstants.Config;
        if (NavPathMatching.IsPathUnder(p, "/modules"))
            return SectionOwnerConstants.Config;
        if (NavPathMatching.IsPathUnder(p, "/attention"))
            return SectionOwnerConstants.Config;
        if (p == "/" || NavPathMatching.IsPathUnder(p, ShellSectionRoutes.DashboardHome))
            return SectionOwnerConstants.Dashboard;

        // Stable order so shared hrefs (e.g. /runs) resolve predictably regardless of dictionary/API key order.
        foreach (var domainName in getAllDomainNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (SectionOwnerConstants.CustomZoneOwners.Contains(domainName))
                continue;

            var menu = getDomain(domainName);
            if (menu?.Items is null) continue;

            if (DomainMenuTree.MenuContainsActiveHref(menu, p))
                return domainName;
        }

        return "";
    }
}
