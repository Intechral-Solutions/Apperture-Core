using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Registers governance services: registry, authorization (RBAC), principal context, and identity persistence.
/// Requires AddApperturePersistence (DbContext) to be registered first.
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
        services.AddScoped<IUserRepository, SqlUserRepository>();
        services.AddScoped<IRoleRepository, SqlRoleRepository>();
        services.AddScoped<IPrincipalToUserResolver, SqlPrincipalToUserResolver>();
        services.AddScoped<IRoleProvider, SqlRoleProvider>();
        services.AddScoped<IAuthorizationPolicyEvaluator, RbacAuthorizationPolicyEvaluator>();
        services.AddScoped<IAuthorizationService, DelegatingAuthorizationService>();
        services.AddScoped<IPrincipalContext, HostPrincipalContext>();
        services.AddScoped<IIdentityResolutionService, PrincipalContextIdentityResolutionService>();
        services.AddScoped<IAuditService, SqlAuditService>();
        services.AddScoped<ILocalCredentialRepository, SqlLocalCredentialRepository>();
        services.AddSingleton<ILocalPasswordHasher, LocalPasswordHasher>();
        services.AddScoped<IFirstAdminBootstrapService, FirstAdminBootstrapService>();
        services.AddSingleton<IConnectorDefinitionRegistry, StubConnectorDefinitionRegistry>();
        return services;
    }
}
