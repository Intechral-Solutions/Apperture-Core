using Apperture.ControlPlane.Dashboard;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>Epic 46: additional real cards for the shared default operator dashboard.</summary>
public sealed class ExecutionsDashboardCardProvider : IDashboardCardProvider
{
    private readonly IRunQueryService _runs;
    private readonly IPrincipalContext _principalContext;

    public ExecutionsDashboardCardProvider(IRunQueryService runs, IPrincipalContext principalContext)
    {
        _runs = runs;
        _principalContext = principalContext;
    }

    public int Order => 25;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return null;

        var from = DateTimeOffset.UtcNow.AddHours(-24);
        var runs = await _runs.ListRunsAsync(new RunQueryFilter(CreatedFrom: from), cancellationToken).ConfigureAwait(false);

        var failed = 0;
        var running = 0;
        var completed = 0;
        var cancelled = 0;
        var other = 0;
        foreach (var r in runs)
        {
            var s = r.Status ?? "";
            if (string.Equals(s, WorkflowLifecycleStatusNames.Failed, StringComparison.OrdinalIgnoreCase))
                failed++;
            else if (string.Equals(s, WorkflowLifecycleStatusNames.Running, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(s, WorkflowLifecycleStatusNames.Parked, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(s, WorkflowLifecycleStatusNames.Requested, StringComparison.OrdinalIgnoreCase))
                running++;
            else if (string.Equals(s, WorkflowLifecycleStatusNames.Completed, StringComparison.OrdinalIgnoreCase))
                completed++;
            else if (string.Equals(s, WorkflowLifecycleStatusNames.Cancelled, StringComparison.OrdinalIgnoreCase))
                cancelled++;
            else
                other++;
        }

        var attention = DashboardCardAttentionConvention.FromVisibleFailedRunsCount(failed);
        var href = failed > 0
            ? $"/runs?status={Uri.EscapeDataString(WorkflowLifecycleStatusNames.Failed)}&createdFrom={Uri.EscapeDataString(from.ToString("o"))}"
            : "/runs";

        return new DashboardCard(
            Id: "executions-24h",
            Type: "summary",
            Title: "Executions (24h)",
            Order: Order,
            NavigationHref: href,
            Attention: attention,
            Summary: runs.Count == 0
                ? "No executions visible to you in the last 24 hours"
                : $"{runs.Count} execution(s) visible to you in the last 24 hours",
            Facts:
            [
                new DashboardCardFact("Visible in window", runs.Count.ToString()),
                new DashboardCardFact("Failed", failed.ToString()),
                new DashboardCardFact("In progress", running.ToString()),
                new DashboardCardFact("Completed", completed.ToString()),
                new DashboardCardFact("Cancelled", cancelled.ToString()),
                new DashboardCardFact("Other statuses", other.ToString()),
                new DashboardCardFact("Data scope", "Executions you can view")
            ],
            NavigationLabel: failed > 0 ? "View failed runs" : "View runs");
    }
}

public sealed class SchedulesDashboardCardProvider : IDashboardCardProvider
{
    private readonly IScheduleGovernanceService _scheduleGovernance;
    private readonly IScheduleManagementService _scheduleManagement;

    public SchedulesDashboardCardProvider(
        IScheduleGovernanceService scheduleGovernance,
        IScheduleManagementService scheduleManagement)
    {
        _scheduleGovernance = scheduleGovernance;
        _scheduleManagement = scheduleManagement;
    }

    public int Order => 35;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var definitions = await _scheduleManagement.ListDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var total = definitions.Count;
        var enabled = definitions.Count(d => d.Enabled);
        var disabled = total - enabled;
        var attention = DashboardCardAttentionConvention.FromScheduleDefinitionCounts(total, enabled);

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
            Facts:
            [
                new DashboardCardFact("Definitions", total.ToString()),
                new DashboardCardFact("Enabled", enabled.ToString()),
                new DashboardCardFact("Disabled", disabled.ToString())
            ],
            NavigationLabel: "Open schedules");
    }
}

public sealed class AuditDashboardCardProvider : IDashboardCardProvider
{
    private readonly IAuditService _audit;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorization;

    public AuditDashboardCardProvider(
        IAuditService audit,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorization)
    {
        _audit = audit;
        _principalContext = principalContext;
        _authorization = authorization;
    }

    public int Order => 36;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        var allowed = await DashboardCardProviderAuth.HasPermissionAsync(
            _principalContext,
            _authorization,
            ActionConstants.AuditRead,
            ResourceTypeConstants.AuditPolicy,
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            return null;

        var from = DateTimeOffset.UtcNow.AddHours(-24);
        var count = await _audit.CountAsync(new AuditQuery(FromUtc: from), cancellationToken).ConfigureAwait(false);
        var attention = DashboardCardAttentionConvention.FromAuditEventCount(count);

        return new DashboardCard(
            Id: "audit-24h",
            Type: "summary",
            Title: "Audit (24h)",
            Order: Order,
            NavigationHref: "/control/audit",
            Attention: attention,
            Summary: count == 0
                ? "No audit events in the last 24 hours"
                : $"{count} audit event(s) in the last 24 hours",
            Facts:
            [
                new DashboardCardFact("Events in window", count.ToString())
            ],
            NavigationLabel: "Open audit timeline");
    }
}

public sealed class GovernanceDashboardCardProvider : IDashboardCardProvider
{
    private readonly IRoleProvider _roles;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorization;

    public GovernanceDashboardCardProvider(
        IRoleProvider roles,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorization)
    {
        _roles = roles;
        _principalContext = principalContext;
        _authorization = authorization;
    }

    public int Order => 37;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        var allowed = await DashboardCardProviderAuth.HasPermissionAsync(
            _principalContext,
            _authorization,
            ActionConstants.UserRead,
            ResourceTypeConstants.Role,
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            return null;

        var all = await _roles.GetRolesAsync(cancellationToken).ConfigureAwait(false);
        var total = all.Count;
        var custom = all.Count(r => !r.IsSystemRole);
        var attention = DashboardCardAttentionConvention.FromRoleCount(total);

        return new DashboardCard(
            Id: "governance-summary",
            Type: "summary",
            Title: "Governance",
            Order: Order,
            NavigationHref: "/config/governance",
            Attention: attention,
            Summary: total == 0
                ? "No roles defined"
                : $"{total} role(s) defined ({custom} custom)",
            Facts:
            [
                new DashboardCardFact("Roles", total.ToString()),
                new DashboardCardFact("Custom roles", custom.ToString())
            ],
            NavigationLabel: "Open governance");
    }
}
