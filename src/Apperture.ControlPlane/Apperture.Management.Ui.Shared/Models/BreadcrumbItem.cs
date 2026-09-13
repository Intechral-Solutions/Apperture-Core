namespace Apperture.Management.Ui.Shared.Models;

/// <summary>
/// One segment in the management UI top-bar breadcrumb trail.
/// </summary>
/// <param name="Label">Display label (generic entity names; not loaded from APIs).</param>
/// <param name="Href">Optional navigation target for parent crumbs.</param>
/// <param name="IsCurrent">When true, rendered as plain text (no link).</param>
public sealed record BreadcrumbItem(
    string Label,
    string? Href = null,
    bool IsCurrent = false)
{
    /// <summary>
    /// True when this crumb should render as a navigable anchor (parent with a non-empty href).
    /// </summary>
    public bool IsLinked =>
        !IsCurrent && !string.IsNullOrWhiteSpace(Href);

    /// <summary>
    /// Stable identity for Blazor <c>@key</c> when the trail is re-rendered after navigation.
    /// </summary>
    public string StableKey =>
        IsCurrent
            ? $"current:{Label}"
            : $"link:{Href ?? ""}:{Label}";
}
