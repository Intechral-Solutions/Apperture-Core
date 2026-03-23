using Apperture.ControlPlane.Dashboard;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

public sealed class SchedulesDashboardCardProvider : IDashboardCardProvider
{
    private readonly IScheduleGovernanceService _scheduleGovernance;
    private readonly IScheduleManagementService _scheduleManagement;
    private readonly IRuntimeHealthQueryService _health;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorization;

    public SchedulesDashboardCardProvider(
        IScheduleGovernanceService scheduleGovernance,
        IScheduleManagementService scheduleManagement,
        IRuntimeHealthQueryService health,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorization)
    {
        _scheduleGovernance = scheduleGovernance;
        _scheduleManagement = scheduleManagement;
        _health = health;
        _principalContext = principalContext;
        _authorization = authorization;
    }

    public int Order => 32;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var definitions = await _scheduleManagement.ListDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var total = definitions.Count;
        var enabled = definitions.Count(d => d.Enabled);
        var disabled = total - enabled;

        var degradedOrBlocked = await TryCountDegradedOrBlockedSchedulesAsync(cancellationToken).ConfigureAwait(false);
        var attention = DashboardCardAttentionConvention.FromScheduleDefinitionCountsAndHealth(total, enabled, degradedOrBlocked ?? 0);

        var facts = new List<DashboardCardFact>
        {
            new("Definitions", total.ToString()),
            new("Enabled", enabled.ToString()),
            new("Disabled", disabled.ToString())
        };
        if (degradedOrBlocked is not null)
            facts.Add(new DashboardCardFact("Degraded or blocked (runtime)", degradedOrBlocked.Value.ToString()));

        return new DashboardCard(
            Id: "schedules-summary",
            Type: "summary",
            Title: "Schedules",
            Order: Order,
            NavigationHref: "/schedules",
            Attention: attention,
            Summary: total == 0
                ? "No schedule definitions"
                : $"{total} schedule definition(s); {enabled} enabled, {disabled} disabled",
            Facts: facts,
            NavigationLabel: "View schedules");
    }

    private async ValueTask<int?> TryCountDegradedOrBlockedSchedulesAsync(CancellationToken cancellationToken)
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
        return snapshot.ScheduleDiagnostics.Count(s =>
            string.Equals(s.ScheduleHealthStatus, ScheduleHealthStatus.Blocked, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.ScheduleHealthStatus, ScheduleHealthStatus.Degraded, StringComparison.OrdinalIgnoreCase));
    }
}
