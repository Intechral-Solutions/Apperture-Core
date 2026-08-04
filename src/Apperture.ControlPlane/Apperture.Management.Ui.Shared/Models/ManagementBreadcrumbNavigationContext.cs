namespace Apperture.Management.Ui.Shared.Models;

/// <summary>
/// Optional, URL-derived navigation context for future drill-through breadcrumb enrichment (UIX-3/4).
/// UIX-2 does not consume this; the resolver overload accepts it so later epics can pass validated
/// query/context without introducing a breadcrumb history store or per-page <c>Set(...)</c> API.
/// </summary>
public sealed record ManagementBreadcrumbNavigationContext
{
    /// <summary>Empty context — identical to calling resolve without context.</summary>
    public static ManagementBreadcrumbNavigationContext None { get; } = new();
}
