using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Seeds the permission catalog with platform permissions per Phase 1 plan Section 4.
/// </summary>
public static class PlatformPermissionSeeder
{
    /// <summary>
    /// Registers the platform permission catalog into the registry.
    /// </summary>
    public static void Seed(IResourceDescriptorRegistry registry)
    {
        Seed(registry, "local");
    }

    /// <summary>
    /// Seeds the permission catalog plus core governance resource descriptors for environment-scoped control.
    /// </summary>
    public static void Seed(IResourceDescriptorRegistry registry, string environmentId)
    {
        SeedCore(registry);

        var envScopeKey = ScopeKeyConstants.Environment(environmentId);

        // Environment governance resource descriptor for this deployment.
        registry.RegisterResource(
            new ResourceDescriptor(ResourceTypeConstants.Environment, envScopeKey),
            ResourceFamily.Core);

        // Environment/runtime governance permissions scoped to the current environment.
        registry.RegisterPermission(
            new PermissionDescriptor(ActionConstants.EnvironmentRead, new ResourceDescriptor(ResourceTypeConstants.Environment, envScopeKey)),
            ResourceFamily.Core);

        registry.RegisterPermission(
            new PermissionDescriptor(ActionConstants.RuntimeRead, new ResourceDescriptor(ResourceTypeConstants.RuntimeInstance, envScopeKey)),
            ResourceFamily.Core);

        registry.RegisterPermission(
            new PermissionDescriptor(ActionConstants.RuntimeEnrollInvitationCreate, new ResourceDescriptor(ResourceTypeConstants.RuntimeInstance, envScopeKey)),
            ResourceFamily.Core);

        // Runtime control (operator control surface)
        registry.RegisterPermission(
            new PermissionDescriptor(ActionConstants.RuntimeControlShutdownRequest, new ResourceDescriptor(ResourceTypeConstants.RuntimeInstance, envScopeKey)),
            ResourceFamily.Core);
    }

    private static void SeedCore(IResourceDescriptorRegistry registry)
    {
        // Integration (Epic 49)
        Register(registry, ActionConstants.IntegrationCreate, ResourceTypeConstants.Integration, ScopeKeyConstants.Instance, ResourceFamily.Core);
        Register(registry, ActionConstants.IntegrationRead, ResourceTypeConstants.Integration, ScopeKeyConstants.Instance, ResourceFamily.Core);
        Register(registry, ActionConstants.IntegrationUpdate, ResourceTypeConstants.Integration, ScopeKeyConstants.Instance, ResourceFamily.Core);
        Register(registry, ActionConstants.IntegrationDelete, ResourceTypeConstants.Integration, ScopeKeyConstants.Instance, ResourceFamily.Core);

        // Connector family
        Register(registry, ActionConstants.ConnectionCreate, ResourceTypeConstants.Connection, ScopeKeyConstants.Instance, ResourceFamily.Connector);
        Register(registry, ActionConstants.ConnectionRead, ResourceTypeConstants.Connection, ScopeKeyConstants.Instance, ResourceFamily.Connector);
        Register(registry, ActionConstants.ConnectionUpdate, ResourceTypeConstants.Connection, ScopeKeyConstants.Instance, ResourceFamily.Connector);
        Register(registry, ActionConstants.ConnectionDelete, ResourceTypeConstants.Connection, ScopeKeyConstants.Instance, ResourceFamily.Connector);
        Register(registry, ActionConstants.ConnectorManage, ResourceTypeConstants.ConnectorDefinition, ScopeKeyConstants.Instance, ResourceFamily.Connector);
        Register(registry, ActionConstants.BindingCreate, ResourceTypeConstants.Binding, ScopeKeyConstants.Instance, ResourceFamily.Connector);
        Register(registry, ActionConstants.BindingRead, ResourceTypeConstants.Binding, ScopeKeyConstants.Instance, ResourceFamily.Connector);
        Register(registry, ActionConstants.BindingDelete, ResourceTypeConstants.Binding, ScopeKeyConstants.Instance, ResourceFamily.Connector);
        Register(registry, ActionConstants.BindingAssign, ResourceTypeConstants.Binding, ScopeKeyConstants.Instance, ResourceFamily.Connector);
        Register(registry, ActionConstants.BindingRevoke, ResourceTypeConstants.Binding, ScopeKeyConstants.Instance, ResourceFamily.Connector);
        Register(registry, ActionConstants.CapabilityRegister, ResourceTypeConstants.CapabilityRegistration, ScopeKeyConstants.Instance, ResourceFamily.Connector);
        Register(registry, ActionConstants.CapabilityConsume, ResourceTypeConstants.CapabilitySlot, ScopeKeyConstants.Instance, ResourceFamily.Connector);
        Register(registry, ActionConstants.MappingManage, ResourceTypeConstants.MappingDefinition, ScopeKeyConstants.Instance, ResourceFamily.Connector);

        // Module
        Register(registry, ActionConstants.ModuleInstall, ResourceTypeConstants.Module, ScopeKeyConstants.Instance, ResourceFamily.Core);
        Register(registry, ActionConstants.ModuleConfigure, ResourceTypeConstants.Module, ScopeKeyConstants.Instance, ResourceFamily.Core);

        // Instance configuration (sign-in UX policy)
        registry.RegisterResource(
            new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance),
            ResourceFamily.Administrative);
        Register(registry, ActionConstants.AuthUiPolicyManage, ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance, ResourceFamily.Administrative);
        Register(registry, ActionConstants.AuthConfigurationRead, ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance, ResourceFamily.Administrative);
        Register(registry, ActionConstants.AuthEntraManage, ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance, ResourceFamily.Administrative);

        // Administrative
        Register(registry, ActionConstants.ScheduleManage, ResourceTypeConstants.Schedule, ScopeKeyConstants.Instance, ResourceFamily.Administrative);
        Register(registry, ActionConstants.RecoveryRead, ResourceTypeConstants.Recovery, ScopeKeyConstants.Instance, ResourceFamily.Administrative);
        Register(registry, ActionConstants.RecoveryManage, ResourceTypeConstants.Recovery, ScopeKeyConstants.Instance, ResourceFamily.Administrative);
        Register(registry, ActionConstants.RuntimeHealthRead, ResourceTypeConstants.RuntimeHealth, ScopeKeyConstants.Instance, ResourceFamily.Administrative);
        Register(registry, ActionConstants.SecretManage, ResourceTypeConstants.SecretDescriptor, ScopeKeyConstants.Instance, ResourceFamily.Administrative);

        // Operation
        Register(registry, ActionConstants.OperationExecute, ResourceTypeConstants.OperationDefinition, ScopeKeyConstants.Instance, ResourceFamily.Operation);
        Register(registry, ActionConstants.OperationRead, ResourceTypeConstants.OperationDefinition, ScopeKeyConstants.Instance, ResourceFamily.Operation);
        Register(registry, ActionConstants.WorkflowStart, ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Instance, ResourceFamily.Operation);

        // Governance
        Register(registry, ActionConstants.RoleManage, ResourceTypeConstants.Role, ScopeKeyConstants.Instance, ResourceFamily.Core);
        Register(registry, ActionConstants.PermissionAssign, ResourceTypeConstants.Role, ScopeKeyConstants.Instance, ResourceFamily.Core);
        Register(registry, ActionConstants.AuditRead, ResourceTypeConstants.AuditPolicy, ScopeKeyConstants.Instance, ResourceFamily.Administrative);
        Register(registry, ActionConstants.RelationshipManage, ResourceTypeConstants.TrustRelationship, ScopeKeyConstants.Instance, ResourceFamily.Trust);

        // Auth administration (Epic 3)
        Register(registry, ActionConstants.UserRead, ResourceTypeConstants.User, ScopeKeyConstants.Instance, ResourceFamily.Core);
        Register(registry, ActionConstants.UserManage, ResourceTypeConstants.User, ScopeKeyConstants.Instance, ResourceFamily.Core);
        Register(registry, ActionConstants.RoleAssign, ResourceTypeConstants.Role, ScopeKeyConstants.Instance, ResourceFamily.Core);
        Register(registry, ActionConstants.CredentialManage, ResourceTypeConstants.User, ScopeKeyConstants.Instance, ResourceFamily.Core);
    }

    private static void Register(
        IResourceDescriptorRegistry registry,
        string action,
        string resourceType,
        string scopeKey,
        ResourceFamily family)
    {
        registry.RegisterPermission(
            new PermissionDescriptor(action, new ResourceDescriptor(resourceType, scopeKey)),
            family);
    }
}
