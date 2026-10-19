using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Idempotent merge logic for the seeded <see cref="BootstrapConstants.PlatformAdminRoleId"/> role. The role ends up holding
/// every registered permission the catalog marks as an administrator default, with its own resource type and scope, plus the
/// fixed grants below (the floor and the named non-catalog <c>workflow.execute</c> exception); the approved obsolete grants are
/// removed and nothing else is. Used by <see cref="PlatformAdminRoleRepairService"/> at startup and covered by unit tests.
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

    /// <summary>What the repair decided for the role: the stored set it should end with, and the difference from the one it found.</summary>
    /// <param name="Target">The permissions to store: what the role holds now minus the obsolete ones, plus what it lacked.</param>
    /// <param name="Added">The grants the role lacked, in identity order.</param>
    /// <param name="Removed">The approved obsolete grants the role held, in identity order.</param>
    public sealed record RepairPlan(
        IReadOnlyList<PermissionDescriptor> Target,
        IReadOnlyList<PermissionDescriptor> Added,
        IReadOnlyList<PermissionDescriptor> Removed)
    {
        /// <summary>True when the stored set would not change. A duplicate in the stored list is not a grant, so it is not a change.</summary>
        public bool IsNoOp => Added.Count == 0 && Removed.Count == 0;
    }

    /// <summary>
    /// Decides what the Platform Administrator role must hold (IAM-10 gate P-6, P-20, P-30(d); IAM-10.2a slice 4). The target is
    /// what the role already holds, plus every registered permission whose definition says
    /// <see cref="PermissionDefinition.GrantedToPlatformAdministratorByDefault"/> (by its own resource type and scope: an
    /// environment permission keeps its real environment, a module permission its module scope), plus the fixed grants of this
    /// class, minus the <see cref="ObsoletePermissions"/>.
    /// </summary>
    /// <remarks>
    /// Only an approved obsolete grant is ever removed; a held grant that is not an administrator default stays. A held grant
    /// counts as held when action and resource type match ignoring case (as the evaluator reads them) and the scope matches
    /// exactly: a grant at another scope is not a stand-in, because the repair makes the stored set explicit. The fixed grants are
    /// the floor and the named non-catalog exception, a backstop that holds even when the registry is incomplete; a test pins that
    /// each of them is a catalog default or that exception.
    /// </remarks>
    public static RepairPlan Plan(
        IReadOnlyList<PermissionDescriptor> current,
        IResourceDescriptorRegistry registry)
    {
        var held = new HashSet<string>(StringComparer.Ordinal);
        var target = new List<PermissionDescriptor>();
        var removed = new List<PermissionDescriptor>();
        foreach (var permission in current)
        {
            if (IsObsolete(permission))
            {
                if (removed.All(r => HoldKey(r) != HoldKey(permission)))
                    removed.Add(permission);
                continue;
            }

            if (held.Add(HoldKey(permission)))
                target.Add(permission);
        }

        var added = new List<PermissionDescriptor>();
        var required = FixedRequiredPermissions()
            .Concat(registry.GetPermissionDefinitions()
                .Where(d => d.GrantedToPlatformAdministratorByDefault)
                .Select(d => d.Identity));
        foreach (var permission in required)
        {
            if (held.Add(HoldKey(permission)))
                added.Add(permission);
        }

        var addedInOrder = added.OrderBy(Identity, StringComparer.Ordinal).ToList();
        target.AddRange(addedInOrder);
        return new RepairPlan(target, addedInOrder, removed.OrderBy(Identity, StringComparer.Ordinal).ToList());
    }

    /// <summary>Applies the repair to <paramref name="current"/> and returns the permissions to store.</summary>
    public static List<PermissionDescriptor> ApplyAllRepairs(
        IReadOnlyList<PermissionDescriptor> current,
        IResourceDescriptorRegistry registry) =>
        Plan(current, registry).Target.ToList();

    private static IEnumerable<PermissionDescriptor> FixedRequiredPermissions() =>
        RequiredAuthAdminPermissions
            .Concat(RequiredOperatorUiPermissions)
            .Concat(RequiredConnectorOperatorPermissions)
            .Concat(RequiredConnectionBrowsePermissions)
            .Concat(RequiredIntegrationPermissions)
            .Concat(RequiredGovernanceSurfacePermissions)
            .Concat(RequiredInstanceAuthConfigurationPermissions)
            .Concat(RequiredManagedContactPermissions)
            .Concat(RequiredNotificationDefinitionPermissions)
            .Concat(RequiredPlatformSettingsPermissions)
            .Concat(RequiredResourceContextPermissions)
            .Concat(RequiredOperationalTeamPermissions)
            .Concat(RequiredDirectorySearchPermissions)
            .Concat(RequiredConnectionVerifyPermissions);

    /// <summary>
    /// An obsolete grant is one whose action and resource type are listed, at any scope (the data step removes it from custom
    /// roles the same way). A grant with no resource is a broad grant, not an obsolete one.
    /// </summary>
    private static bool IsObsolete(PermissionDescriptor permission) =>
        permission.Resource is not null
        && ObsoletePermissions.Any(o =>
            string.Equals(o.Action, permission.Action, StringComparison.OrdinalIgnoreCase)
            && string.Equals(o.Resource!.ResourceType, permission.Resource.ResourceType, StringComparison.OrdinalIgnoreCase));

    private static string Identity(PermissionDescriptor permission) => PermissionSetChange.Identity(permission);

    /// <summary>Action and resource type compared ignoring case, scope compared exactly: the way a stored grant is matched.</summary>
    private static string HoldKey(PermissionDescriptor p) =>
        $"{p.Action.ToLowerInvariant()}\t{p.Resource?.ResourceType?.ToLowerInvariant() ?? ""}\t{(p.Resource is null ? "\0" : p.Resource.ScopeKey)}";

    /// <summary>True if both lists contain the same permission keys (order-insensitive).</summary>
    public static bool EquivalentPermissionSets(
        IReadOnlyList<PermissionDescriptor> a,
        IReadOnlyList<PermissionDescriptor> b)
    {
        var sa = a.Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var sb = b.Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return sa.Count == sb.Count && sa.SequenceEqual(sb, StringComparer.Ordinal);
    }

    private static string Key(PermissionDescriptor p)
    {
        var rt = p.Resource?.ResourceType ?? "";
        var sk = p.Resource?.ScopeKey ?? "";
        return $"{p.Action}\t{rt}\t{sk}";
    }
}
