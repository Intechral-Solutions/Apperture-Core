namespace Apperture.ControlPlane.Dashboard;

public enum DashboardCardAttention
{
    None,
    Info,
    Warning,
    Critical
}

public sealed record DashboardCardFact(string Label, string Value);

public sealed record DashboardCard(
    string Id,
    string Type,
    string Title,
    int Order,
    string? NavigationHref,
    DashboardCardAttention Attention,
    string? Summary,
    IReadOnlyList<DashboardCardFact> Facts,
    bool IsPlaceholder = false,
    string? PlaceholderReason = null,
    /// <summary>When set, dashboard UI uses this for the primary navigation link; otherwise a default label is used.</summary>
    string? NavigationLabel = null);

/// <summary>
/// Shared default operator dashboard view (non-personalized). See Epic 45 ADR.
/// </summary>
public sealed record DashboardDefaultView(
    string Scope,
    string Title,
    IReadOnlyList<DashboardCard> Cards,
    /// <summary>Number of registered card providers that threw while building; other cards may still be valid.</summary>
    int ProviderFailureCount = 0);

public interface IDashboardCardProvider
{
    int Order { get; }

    ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default);
}

public interface IDashboardDefaultViewService
{
    ValueTask<DashboardDefaultView> GetDefaultAsync(CancellationToken cancellationToken = default);
}
