using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Governance checks for recovery/remediation flows. Epic 34.
/// </summary>
public sealed class RecoveryGovernanceService : IRecoveryGovernanceService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;

    public RecoveryGovernanceService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation)
    {
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
    }

    public async ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return false;

        return await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.RecoveryRead, new ResourceDescriptor(ResourceTypeConstants.Recovery, ScopeKeyConstants.Instance)),
                null),
            cancellationToken);
    }

    public async ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return false;

        return await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.RecoveryManage, new ResourceDescriptor(ResourceTypeConstants.Recovery, ScopeKeyConstants.Instance)),
                null),
            cancellationToken);
    }
}
