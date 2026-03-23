using Apperture.ControlPlane.Dashboard;
using Apperture.Core.Application.ControlFacade;
using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

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
        var attention = DashboardCardAttentionConvention.FromRuntimeHealthOverallStatus(snapshot.OverallStatus);

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
                new DashboardCardFact("Degradation reasons", snapshot.DegradationReasons.Count.ToString()),
                new DashboardCardFact("Blocked work", snapshot.BlockedDeferredWork.Count.ToString()),
                new DashboardCardFact("Anomalies", snapshot.Anomalies.Count.ToString())
            ],
            NavigationLabel: "Open control center");
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
        var rawSeverity = items.Select(i => i.Severity).FirstOrDefault() ?? "none";
        var attention = DashboardCardAttentionConvention.FromAttentionSeverityToken(rawSeverity);

        return new DashboardCard(
            Id: "needs-attention",
            Type: "summary",
            Title: "Needs Attention",
            Order: Order,
            NavigationHref: "/control/center",
            Attention: attention,
            Summary: items.Count == 0 ? "No urgent items" : $"{items.Count} item(s) need review",
            Facts:
            [
                new DashboardCardFact("Open attention items", items.Count.ToString()),
                new DashboardCardFact("Highest severity", FormatAttentionSeverity(rawSeverity))
            ],
            NavigationLabel: "Open control center");
    }

    private static string FormatAttentionSeverity(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Equals("none", StringComparison.OrdinalIgnoreCase))
            return "None";
        return raw.ToLowerInvariant() switch
        {
            "high" => "High",
            "medium" => "Medium",
            "low" => "Low",
            _ => char.ToUpperInvariant(raw[0]) + (raw.Length > 1 ? raw[1..].ToLowerInvariant() : "")
        };
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
                new DashboardCardFact("Activity in last 24h", items.Count.ToString()),
                new DashboardCardFact("Latest status", FormatActivityStatus(latest?.CurrentNormalizedStatus))
            ],
            NavigationLabel: "Open control center");
    }

    private static string FormatActivityStatus(string? normalized) => string.IsNullOrWhiteSpace(normalized) || normalized == "none"
        ? "None"
        : char.ToUpperInvariant(normalized[0]) + normalized[1..].ToLowerInvariant();
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
        var attention = DashboardCardAttentionConvention.FromBlockedWorkCount(list.Count);
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
                new DashboardCardFact("Blocked runs", list.Count.ToString()),
                new DashboardCardFact("Retry-eligible now", retryEligible.ToString())
            ],
            NavigationLabel: "Open blocked work");
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
        var noViable = overview.EffectiveExperience == EffectiveAuthExperienceKind.NoViableSignIn;
        var attention = DashboardCardAttentionConvention.FromAuthPosture(noViable, warningCount);
        var experienceLabel = AuthSignInEffectiveExperience.Describe(overview.EffectiveExperience);

        string summary;
        if (noViable)
            summary = "No viable sign-in path — enable local auth or fix Entra.";
        else if (warningCount == 0)
            summary = "No authentication warnings";
        else
            summary = $"{warningCount} warning(s)";

        return new DashboardCard(
            Id: "auth-warnings",
            Type: "summary",
            Title: "Authentication Posture",
            Order: Order,
            NavigationHref: "/config/auth",
            Attention: attention,
            Summary: summary,
            Facts:
            [
                new DashboardCardFact("Sign-in experience", experienceLabel),
                new DashboardCardFact("Warnings", warningCount.ToString())
            ],
            NavigationLabel: "Open authentication settings");
    }
}
