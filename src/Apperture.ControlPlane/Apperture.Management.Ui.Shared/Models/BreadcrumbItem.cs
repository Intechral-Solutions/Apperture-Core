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
    bool IsCurrent = false);
