using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Idempotent startup repair: ensures the platform administrator role has auth-admin permissions
/// exactly once (deduplicates and adds any missing). Safe to run every startup.
/// </summary>
public sealed class PlatformAdminRoleRepairService : IHostedService
{
    private static readonly PermissionDescriptor[] RequiredAuthAdminPermissions =
    [
        new PermissionDescriptor(ActionConstants.UserRead, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance)),
        new PermissionDescriptor(ActionConstants.UserManage, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance)),
        new PermissionDescriptor(ActionConstants.RoleAssign, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance)),
        new PermissionDescriptor(ActionConstants.CredentialManage, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance))
    ];

    private readonly IServiceScopeFactory _scopeFactory;

    public PlatformAdminRoleRepairService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var roleRepository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        var roleId = new RoleId(BootstrapConstants.PlatformAdminRoleId);
        var role = await roleRepository.GetAsync(roleId, cancellationToken);
        if (role is null)
            return;

        var deduped = DeduplicateByActionResourceScope(role.Permissions);
        var withAuthAdmin = EnsureAuthAdminPermissions(deduped);
        if (withAuthAdmin.Count == deduped.Count && deduped.Count == role.Permissions.Count)
            return;

        await roleRepository.UpdatePermissionsAsync(roleId, withAuthAdmin, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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

    private static List<PermissionDescriptor> EnsureAuthAdminPermissions(List<PermissionDescriptor> deduped)
    {
        var byKey = deduped.ToDictionary(Key, p => p, StringComparer.Ordinal);
        foreach (var required in RequiredAuthAdminPermissions)
        {
            var k = Key(required);
            if (!byKey.ContainsKey(k))
                byKey[k] = required;
        }
        return byKey.Values.ToList();
    }
}
