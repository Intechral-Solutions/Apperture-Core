using Apperture.Bootstrap;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
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
        services.AddSingleton<IEnvironmentContext, StaticEnvironmentContext>();
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
        services.AddScoped<IAuthorizationEvaluationService, AuthorizationEvaluationService>();
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
        services.AddScoped<IWorkflowDefinitionResolver, Apperture.ModuleSdk.Abstractions.WorkflowDefinitionResolver>();
        services.AddScoped<IExecutablePermissionEvaluator, ExecutablePermissionEvaluator>();
        services.AddSingleton<IOperationRegistryService, OperationRegistryService>();
        services.AddScoped<IWorkflowPersistence, SqlWorkflowPersistence>();
        services.AddScoped<IWorkflowStartDispatchService, WorkflowStartDispatchService>();
        services.AddScoped<IWorkflowRunReadAuthorizationService, WorkflowRunReadAuthorizationService>();
        services.AddScoped<IWorkflowRunCancelAuthorizationService, WorkflowRunCancelAuthorizationService>();
        services.AddScoped<IWorkflowRunRetryAuthorizationService, WorkflowRunRetryAuthorizationService>();
        services.AddScoped<IWorkflowExternalControlAuthorizationService, WorkflowExternalControlAuthorizationService>();
        services.AddScoped<IWorkflowCancelService, WorkflowCancelService>();
        services.AddScoped<IWorkflowRetryService, WorkflowRetryService>();
        services.AddScoped<IWorkflowExternalControlService, WorkflowExternalControlService>();
        services.AddScoped<IRuntimeControlService, RuntimeControlService>();
        services.AddScoped<IOperationRunReadAuthorizationService, OperationRunReadAuthorizationService>();
        services.AddScoped<IOperationRunCancelAuthorizationService, OperationRunCancelAuthorizationService>();
        services.AddScoped<IOperationDefinitionReadAuthorizationService, OperationDefinitionReadAuthorizationService>();
        services.AddScoped<IOperationDefinitionQueryService, OperationDefinitionQueryService>();
        services.AddScoped<IOperationCancelService, OperationCancelService>();
        services.AddScoped<IExecutionCancelService, ExecutionCancelService>();
        services.AddScoped<IOperationStartAuthorizationService, OperationStartAuthorizationService>();
        services.AddScoped<IOperationInputValidationService, OperationInputValidationService>();
        services.AddScoped<IOperationStartDispatchService, OperationStartDispatchService>();
        services.AddScoped<IRunQueryService, RunQueryService>();
        services.AddScoped<IRunActionAvailabilityQueryService, RunActionAvailabilityQueryService>();
        services.AddScoped<IModuleWorkflowListingService, ModuleWorkflowListingService>();
        services.AddScoped<IWorkflowStartAuthorizationService, WorkflowStartAuthorizationService>();
        services.AddScoped<IScheduleGovernanceService, ScheduleGovernanceService>();
        services.AddScoped<IControlActionAvailabilityProjection, ControlActionAvailabilityProjection>();
        services.AddScoped<IControlActivityQueryService, ControlActivityQueryService>();
        services.AddScoped<IControlAttentionQueryService, ControlAttentionQueryService>();
        services.AddScoped<IControlCenterQueryService, ControlCenterQueryService>();
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
