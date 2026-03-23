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
