using Apperture.ControlPlane.Dashboard;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

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

    public int Order => 34;

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
            NavigationHref: "/control/audit?hours=24",
            Attention: attention,
            Summary: count == 0
                ? "No audit events in the last 24 hours"
                : $"{count} audit event(s) in the last 24 hours",
            Facts:
            [
                new DashboardCardFact("Events in window", count.ToString())
            ],
            NavigationLabel: "View audit timeline");
    }
}
