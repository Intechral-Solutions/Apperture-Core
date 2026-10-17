using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Seeds the permission catalog with platform permissions per Phase 1 plan Section 4. Every entry carries the name,
/// description, category and sensitivity an operator sees.
/// </summary>
/// <remarks>
/// Sensitivity rule. <b>Critical</b>: can change who holds authority, replace credentials or identity trust, read or write
/// secrets, or introduce executable code. <b>Elevated</b>: changes configuration, runtime or alerting behaviour, deletes data,
/// makes an outbound call with a stored credential, or reads the audit log. <b>Standard</b>: reads of operational data and
/// routine authoring. The classification is pinned by <c>PermissionDefinitionCatalogTests</c>.
/// </remarks>
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
        Define(registry, ActionConstants.EnvironmentRead, ResourceTypeConstants.Environment, envScopeKey,
            ResourceFamily.Core, PermissionCategory.Runtime, PermissionSensitivity.Standard,
            "View environment",
            "View the environment this instance runs in.");

        Define(registry, ActionConstants.RuntimeRead, ResourceTypeConstants.RuntimeInstance, envScopeKey,
            ResourceFamily.Core, PermissionCategory.Runtime, PermissionSensitivity.Standard,
            "View runtimes",
            "View the runtimes enrolled in this environment and their status.");

        Define(registry, ActionConstants.RuntimeEnrollInvitationCreate, ResourceTypeConstants.RuntimeInstance, envScopeKey,
            ResourceFamily.Core, PermissionCategory.Runtime, PermissionSensitivity.Elevated,
            "Invite runtimes",
            "Create an invitation that lets a new runtime enroll with this environment.");

        // Runtime control (operator control surface)
        Define(registry, ActionConstants.RuntimeControlShutdownRequest, ResourceTypeConstants.RuntimeInstance, envScopeKey,
            ResourceFamily.Core, PermissionCategory.Runtime, PermissionSensitivity.Elevated,
            "Shut down runtimes",
            "Ask a runtime to shut down, which stops the work it is running.");
    }

    private static void SeedCore(IResourceDescriptorRegistry registry)
    {
        // Integration
        Define(registry, ActionConstants.IntegrationCreate, ResourceTypeConstants.Integration, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Create integrations",
            "Create an integration from an installed module.");
        Define(registry, ActionConstants.IntegrationRead, ResourceTypeConstants.Integration, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.Integrations, PermissionSensitivity.Standard,
            "View integrations",
            "View integrations, their settings and their status.");
        Define(registry, ActionConstants.IntegrationUpdate, ResourceTypeConstants.Integration, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Edit integrations",
            "Change an integration's settings and turn it on or off.");
        Define(registry, ActionConstants.IntegrationDelete, ResourceTypeConstants.Integration, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Delete integrations",
            "Permanently delete an integration.");

        // Connector family
        Define(registry, ActionConstants.ConnectionCreate, ResourceTypeConstants.Connection, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Create connections",
            "Create a connection to an external system, including the credentials it stores.");
        Define(registry, ActionConstants.ConnectionRead, ResourceTypeConstants.Connection, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Standard,
            "View connections",
            "View connections and their settings, without the credentials they store.");
        Define(registry, ActionConstants.ConnectionUpdate, ResourceTypeConstants.Connection, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Edit connections",
            "Change a connection's settings and replace the credentials it stores.");
        Define(registry, ActionConstants.ConnectionDelete, ResourceTypeConstants.Connection, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Delete connections",
            "Permanently delete a connection and the credentials it stores.");
        Define(registry, ActionConstants.ConnectionBrowse, ResourceTypeConstants.Connection, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Browse connection structure",
            "Read the structure of the external system a connection points at, such as its tables and columns, using the credentials the connection stores.");
        Define(registry, ActionConstants.ConnectionVerify, ResourceTypeConstants.Connection, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Test connections",
            "Test that a connection can reach its external system, using the credentials the connection stores.");
        Define(registry, ActionConstants.ConnectorDefinitionRead, ResourceTypeConstants.ConnectorDefinition, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Standard,
            "View connector definitions",
            "View the connector types that connections can be created from and the fields each one needs.");
        Define(registry, ActionConstants.ConnectorManage, ResourceTypeConstants.ConnectorDefinition, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Manage connector definitions",
            "Change the connector definitions that connections are created from.");
        Define(registry, ActionConstants.BindingCreate, ResourceTypeConstants.Binding, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Create bindings",
            "Create a binding that connects a connection to a capability slot.");
        Define(registry, ActionConstants.BindingRead, ResourceTypeConstants.Binding, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Standard,
            "View bindings",
            "View which connections are bound to which capability slots.");
        Define(registry, ActionConstants.BindingDelete, ResourceTypeConstants.Binding, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Delete bindings",
            "Delete a binding between a connection and a capability slot.");
        Define(registry, ActionConstants.BindingAssign, ResourceTypeConstants.Binding, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Assign bindings",
            "Assign a binding so that an integration can use a connection.");
        Define(registry, ActionConstants.BindingRevoke, ResourceTypeConstants.Binding, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Revoke bindings",
            "Withdraw a binding so that an integration can no longer use a connection.");
        Define(registry, ActionConstants.CapabilityRegister, ResourceTypeConstants.CapabilityRegistration, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Register capabilities",
            "Register the capabilities that modules provide to the platform.");
        Define(registry, ActionConstants.CapabilityConsume, ResourceTypeConstants.CapabilitySlot, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Standard,
            "Use capabilities",
            "Use the capabilities that modules provide.");
        Define(registry, ActionConstants.MappingManage, ResourceTypeConstants.MappingDefinition, ScopeKeyConstants.Instance,
            ResourceFamily.Connector, PermissionCategory.Integrations, PermissionSensitivity.Elevated,
            "Manage mappings",
            "Create and change the mappings that translate data between systems.");

        // Module
        Define(registry, ActionConstants.ModuleInstall, ResourceTypeConstants.Module, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.Modules, PermissionSensitivity.Critical,
            "Install modules",
            "Install a module, which adds code that runs on this platform.");
        Define(registry, ActionConstants.ModuleConfigure, ResourceTypeConstants.Module, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.Modules, PermissionSensitivity.Elevated,
            "Configure modules",
            "Change module settings and turn modules on or off.");

        // Instance configuration (sign-in UX policy)
        registry.RegisterResource(
            new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance),
            ResourceFamily.Administrative);
        Define(registry, ActionConstants.AuthUiPolicyManage, ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Authentication, PermissionSensitivity.Elevated,
            "Manage sign-in page policy",
            "Change which sign-in options the sign-in page offers.");
        Define(registry, ActionConstants.AuthConfigurationRead, ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Authentication, PermissionSensitivity.Standard,
            "View authentication configuration",
            "View how sign-in is configured, including the Entra registrations.");
        Define(registry, ActionConstants.AuthEntraManage, ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Authentication, PermissionSensitivity.Critical,
            "Manage Entra registrations",
            "Create, change, activate and delete the Entra registrations that decide who can sign in.");
        Define(registry, ActionConstants.AuthDirectorySearch, ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Authentication, PermissionSensitivity.Elevated,
            "Search the directory",
            "Search the Entra directory for people, using the credentials of the registered tenant.");
        Define(registry, ActionConstants.PlatformSettingsRead, ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Platform, PermissionSensitivity.Standard,
            "View platform settings",
            "View the settings that apply to the whole instance.");
        Define(registry, ActionConstants.PlatformSettingsManage, ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Platform, PermissionSensitivity.Elevated,
            "Manage platform settings",
            "Change or reset the settings that apply to the whole instance.");

        // Administrative
        registry.RegisterResource(
            new ResourceDescriptor(ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance),
            ResourceFamily.Administrative);
        Define(registry, ActionConstants.ResourceContextManage, ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Platform, PermissionSensitivity.Elevated,
            "Manage contexts",
            "Create, rename, describe, archive and restore contexts.");
        Define(registry, ActionConstants.ScheduleManage, ResourceTypeConstants.Schedule, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Operations, PermissionSensitivity.Elevated,
            "Manage schedules",
            "Create, change, turn on and off, and delete the schedules that start workflows.");
        Define(registry, ActionConstants.OperationalTeamManage, ResourceTypeConstants.OperationalTeam, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Operations, PermissionSensitivity.Elevated,
            "Manage operational teams",
            "Create and change the operational teams that receive alerts, and choose who belongs to them.");
        Define(registry, ActionConstants.RecoveryRead, ResourceTypeConstants.Recovery, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Operations, PermissionSensitivity.Standard,
            "View recovery",
            "View work that needs recovery and the recovery options for it.");
        Define(registry, ActionConstants.RecoveryManage, ResourceTypeConstants.Recovery, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Operations, PermissionSensitivity.Elevated,
            "Manage recovery",
            "Act on work that needs recovery, for example by retrying it.");
        Define(registry, ActionConstants.RuntimeHealthRead, ResourceTypeConstants.RuntimeHealth, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Runtime, PermissionSensitivity.Standard,
            "View runtime health",
            "View the health of the runtimes.");
        Define(registry, ActionConstants.SecretManage, ResourceTypeConstants.SecretDescriptor, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Security, PermissionSensitivity.Critical,
            "Manage secrets",
            "Create, replace and delete the secrets the platform stores.");

        // Operation
        Define(registry, ActionConstants.OperationExecute, ResourceTypeConstants.OperationDefinition, ScopeKeyConstants.Instance,
            ResourceFamily.Operation, PermissionCategory.Operations, PermissionSensitivity.Elevated,
            "Run operations",
            "Start operations provided by any module.");
        Define(registry, ActionConstants.OperationRead, ResourceTypeConstants.OperationDefinition, ScopeKeyConstants.Instance,
            ResourceFamily.Operation, PermissionCategory.Operations, PermissionSensitivity.Standard,
            "View operations",
            "View operations and the runs they have produced.");
        Define(registry, ActionConstants.WorkflowStart, ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Instance,
            ResourceFamily.Operation, PermissionCategory.Operations, PermissionSensitivity.Elevated,
            "Start workflows",
            "Start a workflow run.");

        // Governance
        Define(registry, ActionConstants.RoleManage, ResourceTypeConstants.Role, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.AccessControl, PermissionSensitivity.Critical,
            "Manage roles",
            "Create, rename and delete custom roles.");
        Define(registry, ActionConstants.PermissionAssign, ResourceTypeConstants.Role, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.AccessControl, PermissionSensitivity.Critical,
            "Assign permissions to roles",
            "Choose which permissions a custom role grants.");
        Define(registry, ActionConstants.AuditRead, ResourceTypeConstants.AuditPolicy, ScopeKeyConstants.Instance,
            ResourceFamily.Administrative, PermissionCategory.Security, PermissionSensitivity.Elevated,
            "View audit log",
            "Read the audit log of who did what on this instance.");
        Define(registry, ActionConstants.RelationshipManage, ResourceTypeConstants.TrustRelationship, ScopeKeyConstants.Instance,
            ResourceFamily.Trust, PermissionCategory.Security, PermissionSensitivity.Critical,
            "Manage trust relationships",
            "Manage trust relationships with other instances, which this release does not use.",
            administratorDefault: false);

        // Auth administration
        Define(registry, ActionConstants.UserRead, ResourceTypeConstants.User, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.AccessControl, PermissionSensitivity.Standard,
            "View users",
            "View users, their roles and how they sign in.");
        Define(registry, ActionConstants.UserManage, ResourceTypeConstants.User, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.AccessControl, PermissionSensitivity.Elevated,
            "Manage users",
            "Create and change users and link them to sign-in identities.");
        Define(registry, ActionConstants.RoleAssign, ResourceTypeConstants.Role, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.AccessControl, PermissionSensitivity.Critical,
            "Assign roles to users",
            "Give roles to users and take them away.");
        Define(registry, ActionConstants.CredentialManage, ResourceTypeConstants.User, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.AccessControl, PermissionSensitivity.Critical,
            "Manage credentials",
            "Set, reset, disable and re-enable users' local passwords.");

        // Managed contacts
        Define(registry, ActionConstants.ManagedContactRead, ResourceTypeConstants.ManagedContact, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.Notifications, PermissionSensitivity.Standard,
            "View contacts",
            "View and search the people who can receive notifications.");
        Define(registry, ActionConstants.ManagedContactManage, ResourceTypeConstants.ManagedContact, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.Notifications, PermissionSensitivity.Elevated,
            "Manage contacts",
            "Create and change contacts, turn them on or off, and link them to users.");
        Define(registry, ActionConstants.ManagedContactImport, ResourceTypeConstants.ManagedContact, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.Notifications, PermissionSensitivity.Elevated,
            "Import contacts",
            "Import people from the Entra directory as contacts.");

        // Notification definitions
        Define(registry, ActionConstants.NotificationDefinitionRead, ResourceTypeConstants.NotificationDefinition, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.Notifications, PermissionSensitivity.Standard,
            "View notification definitions",
            "View the rules and templates that decide which notifications are sent.");
        Define(registry, ActionConstants.NotificationDefinitionManage, ResourceTypeConstants.NotificationDefinition, ScopeKeyConstants.Instance,
            ResourceFamily.Core, PermissionCategory.Notifications, PermissionSensitivity.Elevated,
            "Manage notification definitions",
            "Create, change and turn on or off the rules and templates that decide which notifications are sent.");
    }

    /// <summary>
    /// Registers one platform permission. The Platform Administrator role is meant to hold every platform permission except the
    /// dormant trust-relationship one, and no platform permission requires step-up yet. The flag is catalog metadata: it does
    /// not change what any role currently holds.
    /// </summary>
    private static void Define(
        IResourceDescriptorRegistry registry,
        string action,
        string resourceType,
        string scopeKey,
        ResourceFamily family,
        PermissionCategory category,
        PermissionSensitivity sensitivity,
        string displayName,
        string description,
        bool administratorDefault = true)
    {
        registry.RegisterPermission(new PermissionDefinition(
            new PermissionDescriptor(action, new ResourceDescriptor(resourceType, scopeKey)),
            displayName,
            description,
            category,
            sensitivity,
            RequiresStepUp: false,
            GrantedToPlatformAdministratorByDefault: administratorDefault,
            Origin: PermissionOrigin.Platform,
            ModuleId: null,
            Family: family));
    }
}
