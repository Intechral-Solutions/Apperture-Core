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

            var children = FilterItems(item.Children, hasCapability);
            if (children.Count == 0 && item.Children is { Count: > 0 } && string.IsNullOrWhiteSpace(item.Href))
                continue;

            kept.Add(new DomainMenuItem
            {
                Id = item.Id,
                Text = item.Text,
                Href = item.Href,
                Icon = item.Icon,
                RequiredCapability = item.RequiredCapability,
                Children = children.Count > 0 ? children : null
            });
        }

        return kept;
    }
}
