namespace Apperture.Core.Domain.Governance;

/// <summary>
/// Well-known action names for authorization. Format: {resourceType}.{action}, lowercase, dot-separated.
/// Per ADR-0013.
/// </summary>
public static class ActionConstants
{
    // Integration (Epic 49)
    public const string IntegrationCreate = "integration.create";
    public const string IntegrationRead = "integration.read";
    public const string IntegrationUpdate = "integration.update";
    public const string IntegrationDelete = "integration.delete";

    /// <summary>
    /// CTX-3B (product decision P2): audit action for a completed Integration hard delete. Deliberately
    /// narrow. Integration create/update/activate/deactivate/retire remain unaudited today; broadening
    /// that is recorded as explicit later debt in the CTX-3B closure, not done here.
    /// </summary>
    public const string IntegrationDeleted = "integration.deleted";

    // Connection
    public const string ConnectionCreate = "connection.create";
    public const string ConnectionRead = "connection.read";
    public const string ConnectionUpdate = "connection.update";
    public const string ConnectionDelete = "connection.delete";

    /// <summary>
    /// Inspect, read-only, the STRUCTURE of the external system a Connection points at (for example the schemas, tables and
    /// columns of a SQL Server database). Additive to <see cref="ConnectionRead"/>: a caller needs both. It is deliberately not
    /// implied by <see cref="ConnectionRead"/>, because reading remote structure discloses more than reading Connection metadata.
    /// It covers structure only; reading remote rows or file content is outside it.
    /// </summary>
    public const string ConnectionBrowse = "connection.browse";

    /// <summary>
    /// Test that a Connection can reach its external system, using the Connection's stored credentials. A separate permission from
    /// <see cref="ConnectionRead"/> because the call is made with a credential on the caller's behalf.
    /// </summary>
    public const string ConnectionVerify = "connection.verify";

    // Connector
    /// <summary>Read the connector-definition catalog (shape metadata) at instance scope. Epic 52A.</summary>
    public const string ConnectorDefinitionRead = "connector.definition.read";

    /// <summary>Administrative actions on connector definitions (reserved for mutating control-plane semantics).</summary>
    public const string ConnectorManage = "connector.manage";

    // Binding
    public const string BindingCreate = "binding.create";
    public const string BindingRead = "binding.read";
    public const string BindingDelete = "binding.delete";
    public const string BindingAssign = "binding.assign";
    public const string BindingRevoke = "binding.revoke";

    // Capability
    public const string CapabilityRegister = "capability.register";
    public const string CapabilityConsume = "capability.consume";

    // Module
    public const string ModuleInstall = "module.install";
    public const string ModuleConfigure = "module.configure";

    // Environment / Runtime
    public const string EnvironmentRead = "environment.read";
    public const string RuntimeRead = "runtime.read";
    public const string RuntimeEnrollInvitationCreate = "runtime.enroll.invitation.create";
    public const string RuntimeControlShutdownRequest = "runtime.control.shutdown.request";

    // Schedule
    public const string ScheduleManage = "schedule.manage";

    /// <summary>Create, change and delete operational teams and their members.</summary>
    public const string OperationalTeamManage = "operational_team.manage";

    // Recovery (Epic 34)
    public const string RecoveryRead = "recovery.read";
    public const string RecoveryManage = "recovery.manage";

    // Runtime health (Epic 34)
    public const string RuntimeHealthRead = "runtime.health.read";

    // Secret
    public const string SecretManage = "secret.manage";

    // Mapping
    public const string MappingManage = "mapping.manage";

    // Operation
    public const string OperationExecute = "operation.execute";
    public const string OperationRead = "operation.read";

    // Workflow
    public const string WorkflowStart = "workflow.start";

    // Governance / Policy
    public const string RoleManage = "role.manage";
    public const string PermissionAssign = "permission.assign";
    public const string AuditRead = "audit.read";
    public const string RelationshipManage = "relationship.manage";

    // Auth administration (Epic 3)
    public const string UserRead = "user.read";
    public const string UserManage = "user.manage";
    public const string RoleAssign = "role.assign";
    public const string CredentialManage = "credential.manage";

    /// <summary>Manage persisted sign-in chooser policy for the management UI (instance-scoped).</summary>
    public const string AuthUiPolicyManage = "auth.ui_policy.manage";

    /// <summary>Read instance authentication configuration and Entra registrations (overview, list).</summary>
    public const string AuthConfigurationRead = "auth.configuration.read";

    /// <summary>Create, update, delete, and activate Entra instance registrations.</summary>
    public const string AuthEntraManage = "auth.entra.manage";

    /// <summary>
    /// Search the Entra directory for people, using the registered tenant's stored credentials. A separate permission from
    /// <see cref="AuthConfigurationRead"/> and <see cref="AuthEntraManage"/> because the call is made with a credential on the
    /// caller's behalf.
    /// </summary>
    public const string AuthDirectorySearch = "auth.directory.search";

    /// <summary>List and search managed contacts (canonical contact layer). Epic 76.</summary>
    public const string ManagedContactRead = "managed_contact.read";

    /// <summary>Create, update, enable/disable, link to users. Epic 76.</summary>
    public const string ManagedContactManage = "managed_contact.manage";

    /// <summary>Import from Entra directory into managed contacts. Epic 76.</summary>
    public const string ManagedContactImport = "managed_contact.import";

    /// <summary>Read notification definitions (instance and integration-scoped). Epic 77.</summary>
    public const string NotificationDefinitionRead = "notification.definition.read";

    /// <summary>Create/update/enable notification definitions. Epic 77.</summary>
    public const string NotificationDefinitionManage = "notification.definition.manage";

    /// <summary>Delete a user-created notification definition (CTX-3B Plan 6a, the Integration-delete blocker's remediation path).</summary>
    public const string NotificationDefinitionDeleted = "notification.definition.deleted";

    /// <summary>Read instance platform settings registry (Phase 1). Epic 109.</summary>
    public const string PlatformSettingsRead = "platform.settings.read";

    /// <summary>Update or reset instance platform settings overrides. Epic 109.</summary>
    public const string PlatformSettingsManage = "platform.settings.manage";

    /// <summary>Create, rename, re-slug, describe, archive, unarchive a ResourceContext (Application). CTX-3A.</summary>
    public const string ResourceContextManage = "resource_context.manage";

    // Resource ownership movement (CTX-3B, design gate §8.8)
    public const string IntegrationOwnershipMovedOut = "integration.ownership.moved_out";
    public const string IntegrationOwnershipMovedIn = "integration.ownership.moved_in";
    public const string IntegrationOwnershipMoveRejected = "integration.ownership.move_rejected";
    public const string ConnectionOwnershipMovedOut = "connection.ownership.moved_out";
    public const string ConnectionOwnershipMovedIn = "connection.ownership.moved_in";
    public const string ConnectionOwnershipMoveRejected = "connection.ownership.move_rejected";
    public const string ScheduleOwnershipMovedOut = "schedule.ownership.moved_out";
    public const string ScheduleOwnershipMovedIn = "schedule.ownership.moved_in";
    public const string ScheduleOwnershipMoveRejected = "schedule.ownership.move_rejected";

    /// <summary>
    /// LFC-1.1: audit action for a completed schedule hard-delete. Written after the delete commits.
    /// </summary>
    public const string ScheduleDeleted = "schedule.deleted";

    /// <summary>
    /// LFC-1.1: audit action for an authorized delete that was refused because the schedule is still enabled.
    /// </summary>
    public const string ScheduleDeleteRejected = "schedule.delete_rejected";
}
