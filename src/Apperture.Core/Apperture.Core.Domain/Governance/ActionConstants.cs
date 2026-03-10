namespace Apperture.Core.Domain.Governance;

/// <summary>
/// Well-known action strings for authorization. Use lowercase, dot-separated.
/// Canonical set for Phase 1 per ADR-0013.
/// </summary>
public static class ActionConstants
{
    // Connection (administrative)
    public const string ConnectionCreate = "connection.create";
    public const string ConnectionRead = "connection.read";
    public const string ConnectionUpdate = "connection.update";
    public const string ConnectionDelete = "connection.delete";

    // Connector
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

    // Schedule
    public const string ScheduleManage = "schedule.manage";

    // Secret
    public const string SecretManage = "secret.manage";

    // Mapping
    public const string MappingManage = "mapping.manage";

    // Operation / Workflow
    public const string OperationExecute = "operation.execute";
    public const string OperationRead = "operation.read";
    public const string WorkflowStart = "workflow.start";

    // Governance
    public const string RoleManage = "role.manage";
    public const string PermissionAssign = "permission.assign";
    public const string AuditRead = "audit.read";
    public const string RelationshipManage = "relationship.manage";
}
