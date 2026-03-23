using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Idempotent startup repair: ensures the platform administrator role has required auth-admin, governance UI,
/// operator-console, and module-catalog permissions (deduplicated). Safe to run every startup.
/// </summary>
public sealed class PlatformAdminRoleRepairService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PlatformAdminRoleRepairService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var roleRepository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        var registry = scope.ServiceProvider.GetRequiredService<IResourceDescriptorRegistry>();
        var roleId = new RoleId(BootstrapConstants.PlatformAdminRoleId);
        var role = await roleRepository.GetAsync(roleId, cancellationToken);
        if (role is null)
            return;

        var repaired = PlatformAdminRolePermissionRepair.ApplyAllRepairs(role.Permissions, registry);
        if (PlatformAdminRolePermissionRepair.EquivalentPermissionSets(role.Permissions, repaired))
            return;

        await roleRepository.UpdatePermissionsAsync(roleId, repaired, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
