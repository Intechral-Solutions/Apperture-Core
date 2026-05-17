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

    // Connection
    public const string ConnectionCreate = "connection.create";
    public const string ConnectionRead = "connection.read";
    public const string ConnectionUpdate = "connection.update";
    public const string ConnectionDelete = "connection.delete";

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
}
