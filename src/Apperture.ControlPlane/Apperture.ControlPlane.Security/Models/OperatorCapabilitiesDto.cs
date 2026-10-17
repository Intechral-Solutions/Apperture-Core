namespace Apperture.ControlPlane.Security.Models;

/// <summary>
/// Operator UI capabilities derived from principal permissions. Epic 34.
/// Epic 40: CanViewGovernance (user.read on Role at instance scope), CanAssignPermissions (permission.assign on Role).
/// Epic 41: CanAssignRoles (role.assign).
/// Epic 43: CanManageAuthUiPolicy (auth.ui_policy.manage on InstanceConfiguration).
/// Epic 44: CanViewAuthConfiguration (auth.configuration.read), CanManageEntraTenants (auth.entra.manage).
/// Epic 48: CanViewConnections (connection.read).
/// Connection authoring: CanCreateConnections (connection.create), CanUpdateConnections (connection.update), CanRetireConnections (connection.delete) at instance scope.
/// Epic 49: CanViewIntegrations (integration.read), CanManageIntegrations (integration.create), CanUpdateIntegrations (integration.update).
/// Epic 52 / 52A: CanViewConnectorDefinitions (connector.definition.read on ConnectorDefinition at instance scope).
/// CTX-3A: CanManageResourceContexts (resource_context.manage on ResourceContext at instance scope).
/// CanBrowseConnections (connection.browse on Connection at instance scope), true only together with CanViewConnections: browsing is additive to reading.
/// Role lifecycle: CanManageRoles (role.manage on Role at instance scope). Grant replacement stays CanAssignPermissions.
/// CanViewGovernance is user.read on User at instance scope (the permission every governance read API enforces).
/// CanSearchDirectory mirrors auth.directory.search on InstanceConfiguration, the one permission the directory-search routes accept.
/// CanVerifyConnections (connection.verify on Connection) is true only together with CanViewConnections, because test-connect needs
/// both: verifying is additive to reading, as browsing is. All capability flags are instance-wide.
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
    bool CanRetireConnections = false,
    bool CanViewManagedContacts = false,
    bool CanManageManagedContacts = false,
    bool CanImportManagedContacts = false,
    bool CanViewNotificationDefinitions = false,
    bool CanManageNotificationDefinitions = false,
    bool CanUpdateIntegrations = false,
    bool CanViewPlatformSettings = false,
    bool CanManagePlatformSettings = false,
    bool CanManageUsers = false,
    bool CanManageCredentials = false,
    bool CanManageResourceContexts = false,
    bool CanBrowseConnections = false,
    bool CanManageRoles = false,
    bool CanSearchDirectory = false,
    bool CanVerifyConnections = false);
