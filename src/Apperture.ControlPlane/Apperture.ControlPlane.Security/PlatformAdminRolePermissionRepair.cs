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
    /// Governance admin UI: permission catalog, role grants, operator capabilities (CanViewGovernance / CanAssignPermissions).
    /// Uses <c>user.read</c> on <see cref="ResourceTypeConstants.Role"/> because no separate governance.read permission exists yet
    /// (see <see cref="OperatorCapabilitiesQueryService"/> and <see cref="GovernanceController"/>).
    /// </summary>
    public static readonly PermissionDescriptor[] RequiredGovernanceSurfacePermissions =
    [
        new(ActionConstants.UserRead, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance)),
        new(ActionConstants.PermissionAssign, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance))
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
        step = EnsureSet(step, RequiredGovernanceSurfacePermissions);
        return step;
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
