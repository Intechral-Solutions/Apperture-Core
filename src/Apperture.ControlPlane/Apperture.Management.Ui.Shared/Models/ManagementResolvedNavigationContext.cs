namespace Apperture.Management.Ui.Shared.Models;

/// <summary>
/// Application-owned navigation data for a validated typed context (UIX-4).
/// Labels and destinations are never taken from the query string.
/// </summary>
public sealed record ManagementResolvedNavigationContext(
    IReadOnlyList<BreadcrumbItem> BreadcrumbPrefix,
    string ReturnHref,
    string ReturnLabel);
