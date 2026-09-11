using Apperture.ControlPlane.Dashboard;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

internal static class RuntimeHealthDashboardNavigation
{
    public static (string Href, string Label) Resolve(RuntimeHealthSnapshot snapshot)
    {
        if (snapshot.BlockedDeferredWork.Count > 0)
            return ("/runs?status=blocked", "Open blocked runs");

        var scheduleAttention = snapshot.ScheduleDiagnostics.Any(s =>
            string.Equals(s.ScheduleHealthStatus, ScheduleHealthStatus.Blocked, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.ScheduleHealthStatus, ScheduleHealthStatus.Degraded, StringComparison.OrdinalIgnoreCase));
        if (scheduleAttention)
            return ("/schedules", "View schedules");

        var runIds = snapshot.Anomalies
            .Select(a => a.RunId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (runIds.Count == 1)
        {
            var id = runIds[0]!.Trim();
            return ($"/runs/{Uri.EscapeDataString(id)}", "Open related run");
        }

        return ("/control/center", "Open control center");
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
        var (href, label) = RuntimeHealthDashboardNavigation.Resolve(snapshot);

        return new DashboardCard(
            Id: "runtime-health",
            Type: "summary",
            Title: "Runtime Health",
            Order: Order,
            NavigationHref: href,
            Attention: attention,
            Summary: snapshot.OverallStatus,
            Facts:
            [
                new DashboardCardFact("Degradation reasons", snapshot.DegradationReasons.Count.ToString()),
                new DashboardCardFact("Blocked work", snapshot.BlockedDeferredWork.Count.ToString()),
                new DashboardCardFact("Anomalies", snapshot.Anomalies.Count.ToString())
            ],
            NavigationLabel: label);
    }
}
