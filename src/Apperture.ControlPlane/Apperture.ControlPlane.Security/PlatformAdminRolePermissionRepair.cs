using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Idempotent merge logic for the seeded <see cref="BootstrapConstants.PlatformAdminRoleId"/> role.
/// Ensures auth-admin, operator-console, governance UI, and module-scoped catalog permissions are present.
/// Used by <see cref="PlatformAdminRoleRepairService"/> at startup and covered by unit tests.
/// </summary>
public static class PlatformAdminRolePermissionRepair
{
    /// <summary>
    /// Auth administration (users, credentials, user↔role assignment). Epic 3.
    /// </summary>
    public static readonly PermissionDescriptor[] RequiredAuthAdminPermissions =
    [
        new(ActionConstants.UserRead, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance)),
        new(ActionConstants.UserManage, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance)),
        new(ActionConstants.RoleAssign, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance)),
        new(ActionConstants.CredentialManage, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance))
    ];

    /// <summary>
    /// Governance admin UI: role grants (CanAssignPermissions). Reading the governance catalog is <c>user.read</c> on User, in
    /// <see cref="RequiredAuthAdminPermissions"/>; <c>user.read</c> on Role, which three consumers used to check, is obsolete
    /// (see <see cref="ObsoletePermissions"/>).
    /// </summary>
    public static readonly PermissionDescriptor[] RequiredGovernanceSurfacePermissions =
    [
        new(ActionConstants.PermissionAssign, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance))
    ];

    /// <summary>
    /// Grants the repair removes from the administrator role when it finds them. Only a listed permission is ever removed; the
    /// repair otherwise only adds. <c>user.read</c> on Role is no longer checked by anything.
    /// </summary>
    public static readonly PermissionDescriptor[] ObsoletePermissions =
    [
        new(ActionConstants.UserRead, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance))
    ];

    /// <summary>Team management (<c>operational_team.manage</c>), which no longer rides on <c>schedule.manage</c>.</summary>
    public static readonly PermissionDescriptor[] RequiredOperationalTeamPermissions =
    [
        new(ActionConstants.OperationalTeamManage, new ResourceDescriptor(ResourceTypeConstants.OperationalTeam, ScopeKeyConstants.Instance))
    ];

    /// <summary>Searching the Entra directory with the registered tenant's stored credentials.</summary>
    public static readonly PermissionDescriptor[] RequiredDirectorySearchPermissions =
    [
        new(ActionConstants.AuthDirectorySearch, new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance))
    ];

    /// <summary>Testing a connection with the credentials it stores. Separate from reading and from browsing.</summary>
    public static readonly PermissionDescriptor[] RequiredConnectionVerifyPermissions =
    [
        new(ActionConstants.ConnectionVerify, new ResourceDescriptor(ResourceTypeConstants.Connection, ScopeKeyConstants.Instance))
    ];

    /// <summary>Managed contacts and Entra import (Epic 76).</summary>
    public static readonly PermissionDescriptor[] RequiredManagedContactPermissions =
    [
        new(ActionConstants.ManagedContactRead, new ResourceDescriptor(ResourceTypeConstants.ManagedContact, ScopeKeyConstants.Instance)),
        new(ActionConstants.ManagedContactManage, new ResourceDescriptor(ResourceTypeConstants.ManagedContact, ScopeKeyConstants.Instance)),
        new(ActionConstants.ManagedContactImport, new ResourceDescriptor(ResourceTypeConstants.ManagedContact, ScopeKeyConstants.Instance))
    ];

    /// <summary>Notification definitions (Epic 77).</summary>
    public static readonly PermissionDescriptor[] RequiredNotificationDefinitionPermissions =
    [
        new(ActionConstants.NotificationDefinitionRead, new ResourceDescriptor(ResourceTypeConstants.NotificationDefinition, ScopeKeyConstants.Instance)),
        new(ActionConstants.NotificationDefinitionManage, new ResourceDescriptor(ResourceTypeConstants.NotificationDefinition, ScopeKeyConstants.Instance))
    ];

    /// <summary>
    /// Config — Authentication UI (sign-in chooser, Entra registrations, auth overview). Epic 41+ operator capabilities.
    /// </summary>
    public static readonly PermissionDescriptor[] RequiredInstanceAuthConfigurationPermissions =
    [
        new(ActionConstants.AuthUiPolicyManage, new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance)),
        new(ActionConstants.AuthConfigurationRead, new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance)),
        new(ActionConstants.AuthEntraManage, new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance))
    ];

    /// <summary>Platform settings registry (Epic 109).</summary>
    public static readonly PermissionDescriptor[] RequiredPlatformSettingsPermissions =
    [
        new(ActionConstants.PlatformSettingsRead, new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance)),
        new(ActionConstants.PlatformSettingsManage, new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance))
    ];

    /// <summary>ResourceContext (Application) management (CTX-3A).</summary>
    public static readonly PermissionDescriptor[] RequiredResourceContextPermissions =
    [
        new(ActionConstants.ResourceContextManage, new ResourceDescriptor(ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance))
    ];

    /// <summary>
    /// Integration management. Epic 49.
    /// </summary>
    public static readonly PermissionDescriptor[] RequiredIntegrationPermissions =
    [
        new(ActionConstants.IntegrationCreate, new ResourceDescriptor(ResourceTypeConstants.Integration, ScopeKeyConstants.Instance)),
        new(ActionConstants.IntegrationRead, new ResourceDescriptor(ResourceTypeConstants.Integration, ScopeKeyConstants.Instance)),
        new(ActionConstants.IntegrationUpdate, new ResourceDescriptor(ResourceTypeConstants.Integration, ScopeKeyConstants.Instance)),
        new(ActionConstants.IntegrationDelete, new ResourceDescriptor(ResourceTypeConstants.Integration, ScopeKeyConstants.Instance))
    ];

    /// <summary>
    /// Connections and connector-definition catalog read (Epic 48, Epic 52, Epic 52A).
    /// </summary>
    public static readonly PermissionDescriptor[] RequiredConnectorOperatorPermissions =
    [
        new(ActionConstants.ConnectionRead, new ResourceDescriptor(ResourceTypeConstants.Connection, ScopeKeyConstants.Instance)),
        new(ActionConstants.ConnectorDefinitionRead, new ResourceDescriptor(ResourceTypeConstants.ConnectorDefinition, ScopeKeyConstants.Instance))
    ];

    /// <summary>
    /// Remote structure browsing on Connections. Held by the platform administrator role (break-glass sessions pass every check
    /// by design); it is deliberately a separate
    /// set from the read/authoring grants above and is never implied by <c>connection.read</c>. Every other role receives it
    /// only by an explicit grant.
    /// </summary>
    public static readonly PermissionDescriptor[] RequiredConnectionBrowsePermissions =
    [
        new(ActionConstants.ConnectionBrowse, new ResourceDescriptor(ResourceTypeConstants.Connection, ScopeKeyConstants.Instance))
    ];

    /// <summary>
    /// Operator console surfaces (menus, modules, workflows, schedules, recovery, audit, runtime health).
    /// </summary>
    public static readonly PermissionDescriptor[] RequiredOperatorUiPermissions =
    [
        new(ActionConstants.ModuleConfigure, new ResourceDescriptor(ResourceTypeConstants.Module, ScopeKeyConstants.Instance)),
        new("workflow.execute", new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Instance)),
        new(ActionConstants.WorkflowStart, new ResourceDescriptor(ResourceTypeConstants.WorkflowInstance, ScopeKeyConstants.Instance)),
        new(ActionConstants.ScheduleManage, new ResourceDescriptor(ResourceTypeConstants.Schedule, ScopeKeyConstants.Instance)),
        new(ActionConstants.RecoveryRead, new ResourceDescriptor(ResourceTypeConstants.Recovery, ScopeKeyConstants.Instance)),
        new(ActionConstants.RecoveryManage, new ResourceDescriptor(ResourceTypeConstants.Recovery, ScopeKeyConstants.Instance)),
        new(ActionConstants.AuditRead, new ResourceDescriptor(ResourceTypeConstants.AuditPolicy, ScopeKeyConstants.Instance)),
        new(ActionConstants.RuntimeHealthRead, new ResourceDescriptor(ResourceTypeConstants.RuntimeHealth, ScopeKeyConstants.Instance))
    ];

    /// <summary>
    /// Applies deduplication and ensures all required permission grants exist.
    /// </summary>
    public static List<PermissionDescriptor> ApplyAllRepairs(
        IReadOnlyList<PermissionDescriptor> current,
        IResourceDescriptorRegistry registry)
    {
        var deduped = DeduplicateByActionResourceScope(current);
        var step = EnsureSet(deduped, RequiredAuthAdminPermissions);
        step = EnsureModuleScopedPermissions(step, registry);
        step = EnsureSet(step, RequiredOperatorUiPermissions);
        step = EnsureSet(step, RequiredConnectorOperatorPermissions);
        step = EnsureSet(step, RequiredConnectionBrowsePermissions);
        step = EnsureSet(step, RequiredIntegrationPermissions);
        step = EnsureSet(step, RequiredGovernanceSurfacePermissions);
        step = EnsureSet(step, RequiredInstanceAuthConfigurationPermissions);
        step = EnsureSet(step, RequiredManagedContactPermissions);
        step = EnsureSet(step, RequiredNotificationDefinitionPermissions);
        step = EnsureSet(step, RequiredPlatformSettingsPermissions);
        step = EnsureSet(step, RequiredResourceContextPermissions);
        step = EnsureSet(step, RequiredOperationalTeamPermissions);
        step = EnsureSet(step, RequiredDirectorySearchPermissions);
        step = EnsureSet(step, RequiredConnectionVerifyPermissions);
        return RemoveObsolete(step);
    }

    private static List<PermissionDescriptor> RemoveObsolete(List<PermissionDescriptor> permissions)
    {
        var obsolete = ObsoletePermissions.Select(Key).ToHashSet(StringComparer.Ordinal);
        return permissions.Where(p => !obsolete.Contains(Key(p))).ToList();
    }

    /// <summary>True if both lists contain the same permission keys (order-insensitive).</summary>
    public static bool EquivalentPermissionSets(
        IReadOnlyList<PermissionDescriptor> a,
        IReadOnlyList<PermissionDescriptor> b)
    {
        var sa = a.Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var sb = b.Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return sa.Count == sb.Count && sa.SequenceEqual(sb, StringComparer.Ordinal);
    }

    private static List<PermissionDescriptor> DeduplicateByActionResourceScope(IReadOnlyList<PermissionDescriptor> permissions)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<PermissionDescriptor>();
        foreach (var p in permissions)
        {
            var key = Key(p);
            if (seen.Add(key))
                result.Add(p);
        }
        return result;
    }

    private static string Key(PermissionDescriptor p)
    {
        var rt = p.Resource?.ResourceType ?? "";
        var sk = p.Resource?.ScopeKey ?? "";
        return $"{p.Action}\t{rt}\t{sk}";
    }

    private static List<PermissionDescriptor> EnsureSet(
        List<PermissionDescriptor> deduped,
        IReadOnlyList<PermissionDescriptor> required)
    {
        var byKey = deduped.ToDictionary(Key, p => p, StringComparer.Ordinal);
        foreach (var r in required)
        {
            var k = Key(r);
            if (!byKey.ContainsKey(k))
                byKey[k] = r;
        }
        return byKey.Values.ToList();
    }

    private static List<PermissionDescriptor> EnsureModuleScopedPermissions(
        List<PermissionDescriptor> current,
        IResourceDescriptorRegistry registry)
    {
        var byKey = current.ToDictionary(Key, p => p, StringComparer.Ordinal);
        var moduleScoped = registry.GetPermissionDescriptors(null)
            .Where(p => p.Resource?.ScopeKey?.StartsWith("module:", StringComparison.Ordinal) == true);
        foreach (var p in moduleScoped)
        {
            var k = Key(p);
            if (!byKey.ContainsKey(k))
                byKey[k] = p;
        }
        return byKey.Values.ToList();
    }
}
