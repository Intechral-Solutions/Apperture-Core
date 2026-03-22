using Apperture.ControlPlane.Security.Models;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Projects operator UI capabilities from principal permissions. Epic 34.
/// </summary>
public sealed class OperatorCapabilitiesQueryService : IOperatorCapabilitiesQueryService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorization;
    private readonly IScheduleGovernanceService _scheduleGovernance;
    private readonly IRecoveryGovernanceService _recoveryGovernance;

    public OperatorCapabilitiesQueryService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorization,
        IScheduleGovernanceService scheduleGovernance,
        IRecoveryGovernanceService recoveryGovernance)
    {
        _principalContext = principalContext;
        _authorization = authorization;
        _scheduleGovernance = scheduleGovernance;
        _recoveryGovernance = recoveryGovernance;
    }

    public async ValueTask<OperatorCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return new OperatorCapabilitiesDto(false, false, false, false, false, false, false);

        var canManageSchedules = await _scheduleGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false);
        var canManageRecovery = await _recoveryGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false);
        var canViewAudit = await EvaluateAsync(actor, ActionConstants.AuditRead, ResourceTypeConstants.AuditPolicy, cancellationToken).ConfigureAwait(false);
        var canViewRuntimeHealth = await EvaluateAsync(actor, ActionConstants.RuntimeHealthRead, ResourceTypeConstants.RuntimeHealth, cancellationToken).ConfigureAwait(false);
        var canManageModules = await EvaluateAsync(actor, ActionConstants.ModuleConfigure, ResourceTypeConstants.Module, cancellationToken).ConfigureAwait(false);
        var canViewGovernance = await EvaluateAsync(actor, ActionConstants.UserRead, ResourceTypeConstants.Role, cancellationToken).ConfigureAwait(false);
        var canAssignPermissions = await EvaluateAsync(actor, ActionConstants.PermissionAssign, ResourceTypeConstants.Role, cancellationToken).ConfigureAwait(false);

        return new OperatorCapabilitiesDto(
            canManageSchedules,
            canManageRecovery,
            canViewAudit,
            canViewRuntimeHealth,
            canManageModules,
            canViewGovernance,
            canAssignPermissions);
    }

    private async Task<bool> EvaluateAsync(IActor actor, string action, string resourceType, CancellationToken cancellationToken) =>
        await _authorization.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(action, new ResourceDescriptor(resourceType, ScopeKeyConstants.Instance)),
                null),
            cancellationToken).ConfigureAwait(false);
}