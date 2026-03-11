using Apperture.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Registers governance services: registry, authorization (Phase 3 RBAC wiring), stub principal context.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds platform authorization and resource governance services.
    /// </summary>
    public static IServiceCollection AddAppertureGovernance(this IServiceCollection services)
    {
        services.AddSingleton<IPlatformPermissionSeeder, PlatformPermissionSeederService>();
        services.AddSingleton<IResourceDescriptorRegistry>(sp =>
        {
            var registry = new InMemoryResourceDescriptorRegistry();
            sp.GetRequiredService<IPlatformPermissionSeeder>().Seed(registry);
            return registry;
        });
        services.AddSingleton<IScopeContainmentEvaluator, ScopeContainmentEvaluator>();
        services.AddScoped<IPrincipalToUserResolver, StubPrincipalToUserResolver>();
        services.AddScoped<IRoleProvider, StubRoleProvider>();
        services.AddScoped<IAuthorizationPolicyEvaluator, RbacAuthorizationPolicyEvaluator>();
        services.AddScoped<IAuthorizationService, DelegatingAuthorizationService>();
        services.AddScoped<IPrincipalContext, StubPrincipalContext>();
        return services;
    }
}
