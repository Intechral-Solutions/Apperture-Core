using Apperture.Management.Ui.Shared.Models;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Drops menu items the operator cannot use. Never a substitute for page/API authorization.
/// </summary>
public static class DomainMenuCapabilityFilter
{
    public static Dictionary<string, DomainMenu> Apply(
        IReadOnlyDictionary<string, DomainMenu> menus,
        Func<string, bool> hasCapability)
    {
        ArgumentNullException.ThrowIfNull(menus);
        ArgumentNullException.ThrowIfNull(hasCapability);

        var result = new Dictionary<string, DomainMenu>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, menu) in menus)
        {
            if (menu is null)
                continue;

            var items = FilterItems(menu.Items, hasCapability);
            if (items.Count == 0 && !SectionOwnerConstants.CustomZoneOwners.Contains(name))
                continue;

            result[name] = new DomainMenu
            {
                Title = menu.Title,
                Items = items
            };
        }

        return result;
    }

    private static List<DomainMenuItem> FilterItems(
        IEnumerable<DomainMenuItem>? items,
        Func<string, bool> hasCapability)
    {
        var kept = new List<DomainMenuItem>();
        if (items is null)
            return kept;

        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.RequiredCapability)
                && !hasCapability(item.RequiredCapability))
                continue;

            var originalChildren = item.Children;
            var children = FilterItems(originalChildren, hasCapability);
            if (children.Count == 0 && originalChildren is { Count: > 0 } && string.IsNullOrWhiteSpace(item.Href))
                continue;

            var href = item.Href;
            if (!string.IsNullOrWhiteSpace(href)
                && originalChildren is { Count: > 0 }
                && originalChildren.Any(c => string.Equals(c.Href, href, StringComparison.OrdinalIgnoreCase))
                && !children.Any(c => string.Equals(c.Href, href, StringComparison.OrdinalIgnoreCase)))
            {
                // The parent's own destination was itself a capability-gated child that got
                // filtered out (e.g. System -> Platform settings). Never leave the parent as a
                // clickable link to a destination the backend will refuse. If other children
                // still survived, keep the parent as an expandable node over them instead of
                // dropping the whole branch; if nothing survived, drop it.
                if (children.Count == 0)
                    continue;
                href = null;
            }

            kept.Add(new DomainMenuItem
            {
                Id = item.Id,
                Text = item.Text,
                Href = href,
                Icon = item.Icon,
                RequiredCapability = item.RequiredCapability,
                Children = children.Count > 0 ? children : null
            });
        }

        return kept;
    }
}
