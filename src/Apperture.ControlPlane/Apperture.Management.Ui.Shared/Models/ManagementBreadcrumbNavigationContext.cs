namespace Apperture.Management.Ui.Shared.Models;

/// <summary>
/// Optional, URL-derived navigation context for future drill-through breadcrumb enrichment (UIX-4).
/// UIX-3 return context uses <see cref="ManagementNavigationReturnContext"/> via
/// <c>AffectedRecordNavigation</c> and does not mutate the top-bar trail.
/// The resolver overload accepts this seam so later epics can pass validated
/// query/context without introducing a breadcrumb history store or per-page <c>Set(...)</c> API.
/// </summary>
public sealed record ManagementBreadcrumbNavigationContext
{
    /// <summary>Empty context — identical to calling resolve without context.</summary>
    public static ManagementBreadcrumbNavigationContext None { get; } = new();
}
