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
    public static string Resolve(
        string path,
        Func<string, DomainMenu?> getDomain,
        IEnumerable<string> getAllDomainNames)
    {
        var p = path.TrimEnd('/');
        if (p.Length == 0) p = "/";

        if (NavPathMatching.IsPathUnder(p, "/config")
            || (NavPathMatching.IsPathUnder(p, ShellSectionRoutes.DashboardHome)
                && !string.Equals(p, ShellSectionRoutes.DashboardHome, StringComparison.OrdinalIgnoreCase)))
            return SectionOwnerConstants.Config;
        if (NavPathMatching.IsPathUnder(p, "/modules"))
            return SectionOwnerConstants.Config;
        if (NavPathMatching.IsPathUnder(p, "/attention"))
            return SectionOwnerConstants.Config;
        if (NavPathMatching.IsPathUnder(p, "/connections"))
            return SectionOwnerConstants.Config;
        if (NavPathMatching.IsPathUnder(p, "/schedules"))
            return SectionOwnerConstants.Config;
        if (NavPathMatching.IsPathUnder(p, BusinessDataUiRoutes.Root)
            || NavPathMatching.IsPathUnder(p, DemoCustomerUiRoutes.LegacyList))
            return SectionOwnerConstants.BusinessData;
        if (NavPathMatching.IsPathUnder(p, "/integrations"))
            return SectionOwnerConstants.Integrations;
        if (p == "/"
            || NavPathMatching.IsPathUnder(p, "/control")
            || NavPathMatching.IsPathUnder(p, "/runs")
            || NavPathMatching.IsPathUnder(p, "/execute")
            || NavPathMatching.IsPathUnder(p, "/runtimes")
            || string.Equals(p, ShellSectionRoutes.DashboardHome, StringComparison.OrdinalIgnoreCase)
            || NavPathMatching.IsPathUnder(p, "/operations")
            || NavPathMatching.IsPathUnder(p, "/workflows"))
            return SectionOwnerConstants.ControlCenter;

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
