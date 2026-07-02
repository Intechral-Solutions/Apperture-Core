using Apperture.ControlPlane.Security.Models;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Projects operator UI capabilities from principal permissions. Epic 34.
/// </summary>
public sealed class OperatorCapabilitiesQueryService : IOperatorCapabilitiesQueryService
{
    private static readonly OperatorCapabilitiesDto DenyAll =
        new(false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);

    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorization;
    private readonly IScheduleGovernanceService _scheduleGovernance;
    private readonly IRecoveryGovernanceService _recoveryGovernance;
    private readonly ILogger<OperatorCapabilitiesQueryService> _logger;

    public OperatorCapabilitiesQueryService(
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorization,
        IScheduleGovernanceService scheduleGovernance,
        IRecoveryGovernanceService recoveryGovernance,
        ILogger<OperatorCapabilitiesQueryService> logger)
    {
        _principalContext = principalContext;
        _authorization = authorization;
        _scheduleGovernance = scheduleGovernance;
        _recoveryGovernance = recoveryGovernance;
        _logger = logger;
    }

    public async ValueTask<OperatorCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
            if (actor is null)
                return DenyAll;

            var canManageSchedules = await _scheduleGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false);
            var canManageRecovery = await _recoveryGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false);
            var canViewAudit = await EvaluateAsync(actor, ActionConstants.AuditRead, ResourceTypeConstants.AuditPolicy, cancellationToken).ConfigureAwait(false);
            var canViewRuntimeHealth = await EvaluateAsync(actor, ActionConstants.RuntimeHealthRead, ResourceTypeConstants.RuntimeHealth, cancellationToken).ConfigureAwait(false);
            var canManageModules = await EvaluateAsync(actor, ActionConstants.ModuleConfigure, ResourceTypeConstants.Module, cancellationToken).ConfigureAwait(false);
            // CanViewGovernance: uses user.read on Role because no distinct governance-read permission exists.
            // Backend governance endpoints rely on user.read. If a more specific governance-read permission is added, update here.
            var canViewGovernance = await EvaluateAsync(actor, ActionConstants.UserRead, ResourceTypeConstants.Role, cancellationToken).ConfigureAwait(false);
            var canAssignPermissions = await EvaluateAsync(actor, ActionConstants.PermissionAssign, ResourceTypeConstants.Role, cancellationToken).ConfigureAwait(false);
            var canAssignRoles = await EvaluateAsync(actor, ActionConstants.RoleAssign, ResourceTypeConstants.Role, cancellationToken).ConfigureAwait(false);
            var canManageAuthUiPolicy = await EvaluateAsync(
                actor,
                ActionConstants.AuthUiPolicyManage,
                ResourceTypeConstants.InstanceConfiguration,
                cancellationToken).ConfigureAwait(false);
            var canViewAuthConfiguration = await EvaluateAsync(
                actor,
                ActionConstants.AuthConfigurationRead,
                ResourceTypeConstants.InstanceConfiguration,
                cancellationToken).ConfigureAwait(false);
            var canManageEntraTenants = await EvaluateAsync(
                actor,
                ActionConstants.AuthEntraManage,
                ResourceTypeConstants.InstanceConfiguration,
                cancellationToken).ConfigureAwait(false);
            var canViewConnections = await EvaluateAsync(
                actor,
                ActionConstants.ConnectionRead,
                ResourceTypeConstants.Connection,
                cancellationToken).ConfigureAwait(false);
            var canCreateConnections = await EvaluateAsync(
                actor,
                ActionConstants.ConnectionCreate,
                ResourceTypeConstants.Connection,
                cancellationToken).ConfigureAwait(false);
            var canUpdateConnections = await EvaluateAsync(
                actor,
                ActionConstants.ConnectionUpdate,
                ResourceTypeConstants.Connection,
                cancellationToken).ConfigureAwait(false);
            var canRetireConnections = await EvaluateAsync(
                actor,
                ActionConstants.ConnectionDelete,
                ResourceTypeConstants.Connection,
                cancellationToken).ConfigureAwait(false);
            var canViewIntegrations = await EvaluateAsync(
                actor,
                ActionConstants.IntegrationRead,
                ResourceTypeConstants.Integration,
                cancellationToken).ConfigureAwait(false);
            var canManageIntegrations = await EvaluateAsync(
                actor,
                ActionConstants.IntegrationCreate,
                ResourceTypeConstants.Integration,
                cancellationToken).ConfigureAwait(false);
            var canUpdateIntegrations = await EvaluateAsync(
                actor,
                ActionConstants.IntegrationUpdate,
                ResourceTypeConstants.Integration,
                cancellationToken).ConfigureAwait(false);
            var canViewConnectorDefinitions = await EvaluateAsync(
                actor,
                ActionConstants.ConnectorDefinitionRead,
                ResourceTypeConstants.ConnectorDefinition,
                cancellationToken).ConfigureAwait(false);
            var canViewManagedContacts = await EvaluateAsync(
                actor,
                ActionConstants.ManagedContactRead,
                ResourceTypeConstants.ManagedContact,
                cancellationToken).ConfigureAwait(false);
            var canManageManagedContacts = await EvaluateAsync(
                actor,
                ActionConstants.ManagedContactManage,
                ResourceTypeConstants.ManagedContact,
                cancellationToken).ConfigureAwait(false);
            var canImportManagedContacts = await EvaluateAsync(
                actor,
                ActionConstants.ManagedContactImport,
                ResourceTypeConstants.ManagedContact,
                cancellationToken).ConfigureAwait(false);
            var canViewNotificationDefinitions = await EvaluateAsync(
                actor,
                ActionConstants.NotificationDefinitionRead,
                ResourceTypeConstants.NotificationDefinition,
                cancellationToken).ConfigureAwait(false);
            var canManageNotificationDefinitions = await EvaluateAsync(
                actor,
                ActionConstants.NotificationDefinitionManage,
                ResourceTypeConstants.NotificationDefinition,
                cancellationToken).ConfigureAwait(false);
            var canViewPlatformSettings = await EvaluateAsync(
                actor,
                ActionConstants.PlatformSettingsRead,
                ResourceTypeConstants.InstanceConfiguration,
                cancellationToken).ConfigureAwait(false);
            var canManagePlatformSettings = await EvaluateAsync(
                actor,
                ActionConstants.PlatformSettingsManage,
                ResourceTypeConstants.InstanceConfiguration,
                cancellationToken).ConfigureAwait(false);
            var canManageUsers = await EvaluateAsync(
                actor,
                ActionConstants.UserManage,
                ResourceTypeConstants.User,
                cancellationToken).ConfigureAwait(false);

            return new OperatorCapabilitiesDto(
                canManageSchedules,
                canManageRecovery,
                canViewAudit,
                canViewRuntimeHealth,
                canManageModules,
                canViewGovernance,
                canAssignPermissions,
                canAssignRoles,
                canManageAuthUiPolicy,
                canViewAuthConfiguration,
                canManageEntraTenants,
                canViewConnections,
                canViewIntegrations,
                canManageIntegrations,
                canViewConnectorDefinitions,
                canCreateConnections,
                canUpdateConnections,
                canRetireConnections,
                canViewManagedContacts,
                canManageManagedContacts,
                canImportManagedContacts,
                canViewNotificationDefinitions,
                canManageNotificationDefinitions,
                canUpdateIntegrations,
                canViewPlatformSettings,
                canManagePlatformSettings,
                canManageUsers);
        }
        catch (OperationCanceledException ex)
        {
            // Common when the Blazor client aborts an in-flight fetch during login/navigation (HTTP 499).
            _logger.LogDebug(ex, "Operator capabilities request canceled; returning deny-all.");
            return DenyAll;
        }
        catch (Exception ex)
        {
            // RBAC/DB failures must not take down the capabilities endpoint (UI relies on it broadly).
            _logger.LogError(ex, "Failed to compute operator capabilities; returning deny-all.");
            return DenyAll;
        }
    }

    private async Task<bool> EvaluateAsync(IActor actor, string action, string resourceType, CancellationToken cancellationToken) =>
        await _authorization.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(action, new ResourceDescriptor(resourceType, ScopeKeyConstants.Instance)),
                null),
            cancellationToken).ConfigureAwait(false);
}