using Apperture.Bootstrap;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Registers governance services: registry, authorization (RBAC), principal context, and identity persistence.
/// Requires AddApperturePersistence (DbContext) to be registered first.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers real recovery authorizer and service (config-based token, RestorePlatformAccess assigns platform-admin to target user).
    /// Call after AddAppertureBootstrap and AddAppertureGovernance so that IBootstrapAuditWriter and persistence are available.
    /// Replaces stub IBootstrapRecoveryAuthorizer and IBootstrapRecoveryService when registered in the same container.
    /// </summary>
    public static IServiceCollection AddAppertureRecovery(this IServiceCollection services)
    {
        services.AddSingleton<IBootstrapRecoveryAuthorizer, ConfigRecoveryAuthorizer>();
        services.AddSingleton<IBootstrapRecoveryService, RealBootstrapRecoveryService>();
        return services;
    }

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
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddHostedService<PlatformAdminRoleRepairService>();
        services.AddHostedService<ModulePermissionRegistrationHostedService>();
        services.AddSingleton<IEnumerable<IModule>>(Array.Empty<IModule>());
        services.AddSingleton<IModuleManifestProvider, KnownModuleManifestProvider>();
        services.AddSingleton<IConnectorDefinitionRegistry, StubConnectorDefinitionRegistry>();
        services.AddSingleton<IWorkflowModuleResolver, WorkflowModuleResolver>();
        services.AddScoped<IWorkflowDefinitionResolver, WorkflowDefinitionResolver>();
        services.AddScoped<IExecutablePermissionEvaluator, ExecutablePermissionEvaluator>();
        services.AddScoped<IWorkflowPersistence, SqlWorkflowPersistence>();
        services.AddScoped<IWorkflowStartDispatchService, WorkflowStartDispatchService>();
        services.AddScoped<IWorkflowRunReadAuthorizationService, WorkflowRunReadAuthorizationService>();
        services.AddScoped<IRunQueryService, RunQueryService>();
        services.AddScoped<IModuleWorkflowListingService, ModuleWorkflowListingService>();
        services.AddScoped<IWorkflowStartAuthorizationService, WorkflowStartAuthorizationService>();
        return services;
    }

    /// <summary>
    /// Registers known modules and the manifest provider for module permission registration at startup.
    /// Call before or after AddAppertureGovernance. Module-declared permissions will be registered into IResourceDescriptorRegistry when the host starts.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="modules">The set of known modules (e.g. from referenced module projects).</param>
    public static IServiceCollection AddKnownModules(this IServiceCollection services, IEnumerable<IModule> modules)
    {
        var list = modules?.ToList() ?? [];
        services.AddSingleton<IEnumerable<IModule>>(list);
        services.AddSingleton<IModuleManifestProvider, KnownModuleManifestProvider>();
        return services;
    }
}
