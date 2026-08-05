namespace Apperture.Management.Ui.Shared.Models;

/// <summary>
/// URL-derived affected-record focus request for Run Detail (UIX-3).
/// Does not alter route-derived breadcrumbs (UIX-4).
/// </summary>
public sealed record AffectedRecordAnchor(
    string? OperationGroupId = null,
    string? OperationItemId = null,
    string? SearchQuery = null)
{
    public static AffectedRecordAnchor Empty { get; } = new();

    public bool HasGroup => !string.IsNullOrWhiteSpace(OperationGroupId);
    public bool HasItem => !string.IsNullOrWhiteSpace(OperationItemId);
    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchQuery);
    public bool IsEmpty => !HasGroup && !HasItem && !HasSearch;
}

/// <summary>
/// Typed return-context descriptor encoded as <c>from</c> + <c>fromKey</c> (never an arbitrary URL).
/// </summary>
public sealed record ManagementNavigationReturnContext(string Type, string Key)
{
    public const string DemoCustomerType = "demo-customer";
}

/// <summary>Resolved in-page return affordance for a validated navigation context.</summary>
public sealed record ManagementNavigationReturnAffordance(
    string Label,
    string Href,
    string AriaLabel);
