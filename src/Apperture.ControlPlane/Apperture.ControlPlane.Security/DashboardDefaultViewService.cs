using Apperture.Core.Application.ControlFacade;
using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

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
    string? PlaceholderReason = null);

public sealed record DashboardDefaultView(
    string Scope,
    string Title,
    IReadOnlyList<DashboardCard> Cards);

public interface IDashboardCardProvider
{
    int Order { get; }

    ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default);
}

public interface IDashboardDefaultViewService
{
    ValueTask<DashboardDefaultView> GetDefaultAsync(CancellationToken cancellationToken = default);
}

public sealed class DashboardDefaultViewService : IDashboardDefaultViewService
{
    private readonly IEnumerable<IDashboardCardProvider> _providers;

    public DashboardDefaultViewService(IEnumerable<IDashboardCardProvider> providers) => _providers = providers;

    public async ValueTask<DashboardDefaultView> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var cards = new List<DashboardCard>();
        foreach (var provider in _providers.OrderBy(p => p.Order))
        {
            var card = await provider.BuildAsync(cancellationToken).ConfigureAwait(false);
            if (card is not null)
                cards.Add(card);
        }

        return new DashboardDefaultView(
            Scope: "shared-platform-operator",
            Title: "Platform Summary",
            Cards: cards);
    }
}

internal static class DashboardCardProviderAuth
{
    public static async ValueTask<bool> HasPermissionAsync(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorization,
        string action,
        string resourceType,
        CancellationToken cancellationToken)
    {
        var actor = await principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return false;

        var permission = new PermissionDescriptor(
            action,
            new ResourceDescriptor(resourceType, ScopeKeyConstants.Instance));
        return await authorization.EvaluateAsync(
            new AuthorizationEvaluationContext(actor, permission, permission.Resource),
            cancellationToken).ConfigureAwait(false);
    }
}

public sealed class RuntimeHealthDashboardCardProvider : IDashboardCardProvider
{
    private readonly IRuntimeHealthQueryService _health;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorization;

    public RuntimeHealthDashboardCardProvider(
        IRuntimeHealthQueryService health,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorization)
    {
        _health = health;
        _principalContext = principalContext;
        _authorization = authorization;
    }

    public int Order => 10;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        var allowed = await DashboardCardProviderAuth.HasPermissionAsync(
            _principalContext,
            _authorization,
            ActionConstants.RuntimeHealthRead,
            ResourceTypeConstants.RuntimeHealth,
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            return null;

        var snapshot = await _health.GetSnapshotAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var attention = snapshot.OverallStatus switch
        {
            RuntimeHealthStatus.Unhealthy => DashboardCardAttention.Critical,
            RuntimeHealthStatus.Degraded => DashboardCardAttention.Warning,
            _ => DashboardCardAttention.None
        };

        return new DashboardCard(
            Id: "runtime-health",
            Type: "summary",
            Title: "Runtime Health",
            Order: Order,
            NavigationHref: "/control/center",
            Attention: attention,
            Summary: snapshot.OverallStatus,
            Facts:
            [
                new("Degradation reasons", snapshot.DegradationReasons.Count.ToString()),
                new("Blocked work", snapshot.BlockedDeferredWork.Count.ToString()),
                new("Anomalies", snapshot.Anomalies.Count.ToString())
            ]);
    }
}

public sealed class AttentionDashboardCardProvider : IDashboardCardProvider
{
    private readonly IControlAttentionQueryService _attention;

    public AttentionDashboardCardProvider(IControlAttentionQueryService attention) => _attention = attention;

    public int Order => 20;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        var items = await _attention.ListAttentionAsync(DateTimeOffset.UtcNow.AddHours(-24), 20, cancellationToken).ConfigureAwait(false);
        var topSeverity = items.Select(i => i.Severity).FirstOrDefault() ?? "none";
        var attention = topSeverity switch
        {
            "high" => DashboardCardAttention.Critical,
            "medium" => DashboardCardAttention.Warning,
            "low" => DashboardCardAttention.Info,
            _ => DashboardCardAttention.None
        };

        return new DashboardCard(
            Id: "needs-attention",
            Type: "summary",
            Title: "Needs Attention",
            Order: Order,
            NavigationHref: "/control/blocked-work",
            Attention: attention,
            Summary: items.Count == 0 ? "No urgent items" : $"{items.Count} item(s) need review",
            Facts:
            [
                new("Open attention items", items.Count.ToString()),
                new("Highest severity", topSeverity)
            ]);
    }
}

public sealed class RecentActivityDashboardCardProvider : IDashboardCardProvider
{
    private readonly IControlActivityQueryService _activity;

    public RecentActivityDashboardCardProvider(IControlActivityQueryService activity) => _activity = activity;

    public int Order => 30;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        var items = await _activity.ListRecentActivityAsync(DateTimeOffset.UtcNow.AddHours(-24), 25, cancellationToken).ConfigureAwait(false);
        var latest = items.FirstOrDefault();
        return new DashboardCard(
            Id: "recent-activity",
            Type: "summary",
            Title: "Recent Activity",
            Order: Order,
            NavigationHref: "/control/center",
            Attention: DashboardCardAttention.None,
            Summary: latest is null
                ? "No activity in the last 24 hours"
                : $"Latest: {latest.OutcomeSummary}",
            Facts:
            [
                new("Activity in last 24h", items.Count.ToString()),
                new("Latest status", latest?.CurrentNormalizedStatus ?? "none")
            ]);
    }
}

public sealed class BlockedWorkDashboardCardProvider : IDashboardCardProvider
{
    private readonly IRecoveryQueryService _recovery;
    private readonly IRecoveryGovernanceService _recoveryGovernance;

    public BlockedWorkDashboardCardProvider(IRecoveryQueryService recovery, IRecoveryGovernanceService recoveryGovernance)
    {
        _recovery = recovery;
        _recoveryGovernance = recoveryGovernance;
    }

    public int Order => 40;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        if (!await _recoveryGovernance.CanReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var list = await _recovery.GetBlockedWorkRemediationAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var retryEligible = list.Count(x => x.RetryEligibleNow);
        var attention = list.Count > 0 ? DashboardCardAttention.Warning : DashboardCardAttention.None;
        return new DashboardCard(
            Id: "blocked-work",
            Type: "summary",
            Title: "Blocked Work",
            Order: Order,
            NavigationHref: "/control/blocked-work",
            Attention: attention,
            Summary: list.Count == 0 ? "No blocked work" : $"{list.Count} blocked run(s)",
            Facts:
            [
                new("Blocked runs", list.Count.ToString()),
                new("Retry-eligible now", retryEligible.ToString())
            ]);
    }
}

public sealed class AuthWarningsDashboardCardProvider : IDashboardCardProvider
{
    private readonly IAuthenticationAdminOverviewService _overview;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorization;

    public AuthWarningsDashboardCardProvider(
        IAuthenticationAdminOverviewService overview,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorization)
    {
        _overview = overview;
        _principalContext = principalContext;
        _authorization = authorization;
    }

    public int Order => 50;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        var canRead = await DashboardCardProviderAuth.HasPermissionAsync(
            _principalContext,
            _authorization,
            ActionConstants.AuthConfigurationRead,
            ResourceTypeConstants.InstanceConfiguration,
            cancellationToken).ConfigureAwait(false);
        var canPolicyManage = await DashboardCardProviderAuth.HasPermissionAsync(
            _principalContext,
            _authorization,
            ActionConstants.AuthUiPolicyManage,
            ResourceTypeConstants.InstanceConfiguration,
            cancellationToken).ConfigureAwait(false);
        var canEntraManage = await DashboardCardProviderAuth.HasPermissionAsync(
            _principalContext,
            _authorization,
            ActionConstants.AuthEntraManage,
            ResourceTypeConstants.InstanceConfiguration,
            cancellationToken).ConfigureAwait(false);
        if (!canRead && !canPolicyManage && !canEntraManage)
            return null;

        var overview = await _overview.GetAsync(cancellationToken).ConfigureAwait(false);
        var warningCount = overview.Warnings.Count;
        var attention = warningCount > 0 ? DashboardCardAttention.Warning : DashboardCardAttention.None;

        return new DashboardCard(
            Id: "auth-warnings",
            Type: "summary",
            Title: "Authentication Posture",
            Order: Order,
            NavigationHref: "/config/auth",
            Attention: attention,
            Summary: warningCount == 0 ? "No authentication warnings" : $"{warningCount} warning(s)",
            Facts:
            [
                new("Effective experience", overview.EffectiveExperience.ToString()),
                new("Warnings", warningCount.ToString())
            ]);
    }
}
