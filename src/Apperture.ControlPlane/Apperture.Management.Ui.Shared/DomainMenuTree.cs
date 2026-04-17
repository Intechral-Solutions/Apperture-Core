using Apperture.Management.Ui.Shared.Models;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Helpers for recursive control panel menus (active path, section resolution).
/// </summary>
public static class DomainMenuTree
{
    /// <summary>
    /// Expand/collapse state key: explicit <see cref="DomainMenuItem.Id"/> if set, else domain + index path.
    /// </summary>
    public static string GetExpandStateKey(string domainName, string indexPath, DomainMenuItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Id))
            return $"{domainName}:{item.Id}";
        return $"{domainName}/{indexPath}";
    }

    /// <summary>
    /// True if <paramref name="path"/> matches this node's <see cref="DomainMenuItem.Href"/> or any descendant's href
    /// (same rules as <see cref="NavPathMatching.PathMatchesMenuHref"/>).
    /// </summary>
    public static bool SubtreeContainsActiveHref(DomainMenuItem node, string path)
    {
        if (!string.IsNullOrWhiteSpace(node.Href) && NavPathMatching.PathMatchesMenuHref(path, node.Href))
            return true;
        if (node.Children is null || node.Children.Count == 0)
            return false;
        return node.Children.Any(c => SubtreeContainsActiveHref(c, path));
    }

    /// <summary>
    /// True if any node in the domain menu tree matches the path (used for section ownership).
    /// </summary>
    public static bool MenuContainsActiveHref(DomainMenu? menu, string path)
    {
        if (menu?.Items is null || menu.Items.Count == 0)
            return false;
        return menu.Items.Any(item => SubtreeContainsActiveHref(item, path));
    }
}
