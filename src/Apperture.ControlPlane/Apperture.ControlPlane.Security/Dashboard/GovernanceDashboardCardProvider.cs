using Apperture.ControlPlane.Dashboard;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

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

    public int Order => 36;

    public async ValueTask<DashboardCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        var allowed = await DashboardCardProviderAuth.HasPermissionAsync(
            _principalContext,
            _authorization,
            ActionConstants.UserRead,
            ResourceTypeConstants.User,
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
            NavigationHref: "/config/governance/roles",
            Attention: attention,
            Summary: total == 0
                ? "No roles defined"
                : $"{total} role(s) defined ({custom} custom)",
            Facts:
            [
                new DashboardCardFact("Roles", total.ToString()),
                new DashboardCardFact("Custom roles", custom.ToString())
            ],
            NavigationLabel: "View roles");
    }
}
