using Apperture.ControlPlane.Dashboard;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

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

    public int Order => 38;

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
            NavigationLabel: "View authentication settings");
    }
}
