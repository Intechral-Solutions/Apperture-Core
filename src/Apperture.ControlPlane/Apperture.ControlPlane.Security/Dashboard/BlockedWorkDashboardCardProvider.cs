using Apperture.ControlPlane.Dashboard;
using Apperture.Core.Application.Recovery;

namespace Apperture.ControlPlane.Security;

public sealed class BlockedWorkDashboardCardProvider : IDashboardCardProvider
{
    private readonly IRecoveryQueryService _recovery;
    private readonly IRecoveryGovernanceService _recoveryGovernance;

    public BlockedWorkDashboardCardProvider(IRecoveryQueryService recovery, IRecoveryGovernanceService recoveryGovernance)
    {
        _recovery = recovery;
        _recoveryGovernance = recoveryGovernance;
    }

    public int Order => 12;

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
