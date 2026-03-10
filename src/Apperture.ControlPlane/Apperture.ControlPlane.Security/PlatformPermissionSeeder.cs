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

        // Administrative
        Register(registry, ActionConstants.ScheduleManage, ResourceTypeConstants.Schedule, ScopeKeyConstants.Instance, ResourceFamily.Administrative);
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
