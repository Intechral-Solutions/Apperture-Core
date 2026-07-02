using Apperture.ControlPlane.Dashboard;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

internal static class IntegrationFleetDashboardNavigation
{
    public static (string Href, string Label) Resolve(IReadOnlyList<IntegrationFleetRowProjection> fleet)
    {
        if (fleet.Count == 0)
            return ("/integrations", "Open integrations");

        var needsReview = fleet
            .Where(r =>
                r.Readiness.BlockingIssueCount > 0
                || !r.Readiness.IsRunnable
                || r.AttentionTokens.Count > 0
                || r.OperationalRuns.HasRecentOperationalFailure
                || r.ExpectedRuns.HasUnexpectedSilence)
            .ToList();

        if (needsReview.Count == 1)
        {
            var id = needsReview[0].Id;
            return ($"/integrations/{Uri.EscapeDataString(id)}", "Open integration");
        }

        if (needsReview.Count > 1)
            return ("/integrations", "Review integrations");

        return ("/integrations", "View integrations");
    }
}

/// <summary>Epic S4: integration fleet health summary for the operator dashboard.</summary>
public sealed class IntegrationsDashboardCardProvider : IDashboardCardProvider
{
    private readonly IIntegrationReadinessService _readiness;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorization;

    public IntegrationsDashboardCardProvider(
        IIntegrationReadinessService readiness,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorization)
    {
        _readiness = readiness;
        _principalContext = principalContext;
        _authorization = authorization;
    }

    public int Order => 14;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        var allowed = await DashboardCardProviderAuth.HasPermissionAsync(
            _principalContext,
            _authorization,
            ActionConstants.IntegrationRead,
            ResourceTypeConstants.Integration,
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            return null;

        var fleet = await _readiness.ListFleetAsync(new IntegrationFleetQuery(), cancellationToken).ConfigureAwait(false);
        var total = fleet.Count;
        var blocking = fleet.Count(r => r.Readiness.BlockingIssueCount > 0);
        var notRunnable = fleet.Count(r => !r.Readiness.IsRunnable);
        var opFailures = fleet.Count(r => r.OperationalRuns.HasRecentOperationalFailure);
        var silence = fleet.Count(r => r.ExpectedRuns.HasUnexpectedSilence);
        var attention = DashboardCardAttentionConvention.FromIntegrationFleetSignals(
            blocking,
            opFailures,
            silence,
            notRunnable);
        var (href, label) = IntegrationFleetDashboardNavigation.Resolve(fleet);

        return new DashboardCard(
            Id: "integrations-fleet",
            Type: "summary",
            Title: "Integrations",
            Order: Order,
            NavigationHref: href,
            Attention: attention,
            Summary: total == 0
                ? "No integrations configured yet"
                : blocking > 0 || silence > 0
                    ? $"{total} integration(s); {blocking + silence} need attention"
                    : $"{total} integration(s) in fleet",
            Facts:
            [
                new DashboardCardFact("Total", total.ToString()),
                new DashboardCardFact("Not runnable", notRunnable.ToString()),
                new DashboardCardFact("Blocking issues", blocking.ToString()),
                new DashboardCardFact("Recent failures (7d)", opFailures.ToString()),
                new DashboardCardFact("Expected-run silence", silence.ToString())
            ],
            NavigationLabel: label);
    }
}
