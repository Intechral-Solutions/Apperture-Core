namespace Apperture.Core.Domain.Governance;

/// <summary>
/// Well-known resource type names for authorization. Use PascalCase.
/// Extensible via registration; these are the initial platform types.
/// </summary>
public static class ResourceTypeConstants
{
    // Core / Environment
    public const string Environment = "Environment";
    public const string RuntimeInstance = "RuntimeInstance";
    public const string Module = "Module";
    public const string ModuleInstallation = "ModuleInstallation";
    public const string ModuleInstance = "ModuleInstance";

    // Identity
    public const string User = "User";
    /// <summary>Canonical person/contact record (Epic 76); distinct from login-capable User resource.</summary>
    public const string ManagedContact = "ManagedContact";
    public const string Role = "Role";
    public const string ServiceAccount = "ServiceAccount";

    /// <summary>Instance-wide operator configuration (sign-in UX policy, etc.).</summary>
    public const string InstanceConfiguration = "InstanceConfiguration";

    // Administrative
    public const string Schedule = "Schedule";
    public const string Recovery = "Recovery";
    public const string RuntimeHealth = "RuntimeHealth";
    public const string SecretDescriptor = "SecretDescriptor";
    public const string AuditPolicy = "AuditPolicy";

    // Trust / Relationship
    public const string TrustRelationship = "TrustRelationship";
    public const string ExternalInstance = "ExternalInstance";
    public const string GrantedCapability = "GrantedCapability";

    /// <summary>Integration — configured instance of a module (Epic 49).</summary>
    public const string Integration = "Integration";

    // Connector family
    public const string ConnectorDefinition = "ConnectorDefinition";
    public const string Connection = "Connection";
    public const string CapabilityRegistration = "CapabilityRegistration";
    public const string CapabilitySlot = "CapabilitySlot";
    public const string Binding = "Binding";
    public const string MappingDefinition = "MappingDefinition";

    // Operational
    public const string OperationDefinition = "OperationDefinition";
    public const string OperationRun = "OperationRun";
    public const string WorkflowInstance = "WorkflowInstance";
}
