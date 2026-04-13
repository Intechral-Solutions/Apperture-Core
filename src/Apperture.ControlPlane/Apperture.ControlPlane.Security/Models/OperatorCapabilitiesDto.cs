namespace Apperture.ControlPlane.Security.Models;

/// <summary>
/// Operator UI capabilities derived from principal permissions. Epic 34.
/// Epic 40: CanViewGovernance (user.read on Role at instance scope), CanAssignPermissions (permission.assign on Role).
/// Epic 41: CanAssignRoles (role.assign).
/// Epic 43: CanManageAuthUiPolicy (auth.ui_policy.manage on InstanceConfiguration).
/// Epic 44: CanViewAuthConfiguration (auth.configuration.read), CanManageEntraTenants (auth.entra.manage).
/// Epic 48: CanViewConnections (connection.read).
/// Connection authoring: CanCreateConnections (connection.create), CanUpdateConnections (connection.update), CanRetireConnections (connection.delete) at instance scope.
/// Epic 49: CanViewIntegrations (integration.read), CanManageIntegrations (integration.create or integration.update).
/// Epic 52 / 52A: CanViewConnectorDefinitions (connector.definition.read on ConnectorDefinition at instance scope).
/// </summary>
public sealed record OperatorCapabilitiesDto(
    bool CanManageSchedules,
    bool CanManageRecovery,
    bool CanViewAudit,
    bool CanViewRuntimeHealth,
    bool CanManageModules,
    bool CanViewGovernance = false,
    bool CanAssignPermissions = false,
    bool CanAssignRoles = false,
    bool CanManageAuthUiPolicy = false,
    bool CanViewAuthConfiguration = false,
    bool CanManageEntraTenants = false,
    bool CanViewConnections = false,
    bool CanViewIntegrations = false,
    bool CanManageIntegrations = false,
    bool CanViewConnectorDefinitions = false,
    bool CanCreateConnections = false,
    bool CanUpdateConnections = false,
    bool CanRetireConnections = false);
