using Apperture.Bootstrap;
using Apperture.ControlPlane.Dashboard;
using Apperture.ControlPlane.Security.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformSettings;
using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
        services.AddScoped<IInstanceAuthUiPolicyRepository, SqlInstanceAuthUiPolicyRepository>();
        services.AddScoped<IEntraInstanceRegistrationRepository, SqlEntraInstanceRegistrationRepository>();
        services.AddHttpClient(EntraOpenIdConnectConfigurationValidator.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddSingleton<IEntraOpenIdConnectConfigurationValidator, EntraOpenIdConnectConfigurationValidator>();
        services.AddSingleton<IEntraDirectoryDiscoveryService, EntraDirectoryDiscoveryService>();
        services.AddSingleton<IPostConfigureOptions<EntraAuthOptions>, EntraAuthOptionsDatabasePostConfigure>();
        services.AddScoped<IAuthenticationAdminOverviewService, AuthenticationAdminOverviewService>();
        services.AddScoped<IUserRepository, SqlUserRepository>();
        services.AddScoped<IManagedContactRepository, SqlManagedContactRepository>();
        services.AddScoped<IManagedContactGroupRepository, SqlManagedContactGroupRepository>();
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
        services.AddScoped<IManagedContactManagementService, ManagedContactManagementService>();
        // Module permission registration must run before role repair so platform-admin gets module-scoped workflow.execute etc.
        services.AddHostedService<ModulePermissionRegistrationHostedService>();
        services.AddHostedService<PlatformAdminRoleRepairService>();
        services.AddHostedService<BootstrapPlatformAdminAssignmentHostedService>();
        services.AddHostedService<DevTestUserBootstrapHostedService>();
        services.AddSingleton<IEnumerable<IModule>>(Array.Empty<IModule>());
        services.AddScoped<IModuleRegistryService, SqlModuleRegistryService>();
        services.AddSingleton<IModuleManifestProvider, KnownModuleManifestProvider>();
        services.AddSingleton<IModuleActivationFilter, RegistryBackedModuleActivationFilter>();
        services.AddSingleton<IConnectorDefinitionRegistry, StubConnectorDefinitionRegistry>();
        services.AddHostedService<SeedWellKnownConnectorDefinitionsHostedService>();
        services.AddHostedService<ConnectorDefinitionContributorsHostedService>();
        services.AddHostedService<ConnectionVerifierRegistrationSanityHostedService>();
        services.AddSingleton<IWorkflowModuleResolver, WorkflowModuleResolver>();
        services.AddScoped<IWorkflowDefinitionResolver, Apperture.ModuleSdk.Abstractions.WorkflowDefinitionResolver>();
        services.AddScoped<IExecutablePermissionEvaluator, ExecutablePermissionEvaluator>();
        services.AddSingleton<IOperationRegistryService, OperationRegistryService>();
        services.AddSingleton<IProcedureRegistryService, ProcedureRegistryService>();
        services.AddSqlWorkflowDispatchServices();
        services.AddScoped<IWorkflowRunReadAuthorizationService, WorkflowRunReadAuthorizationService>();
        services.AddScoped<IWorkflowRunCancelAuthorizationService, WorkflowRunCancelAuthorizationService>();
        services.AddScoped<IWorkflowRunRetryAuthorizationService, WorkflowRunRetryAuthorizationService>();
        services.AddScoped<IWorkflowExternalControlAuthorizationService, WorkflowExternalControlAuthorizationService>();
        services.AddScoped<IWorkflowCancelService, WorkflowCancelService>();
        services.AddScoped<IWorkflowRetryService, WorkflowRetryService>();
        services.AddScoped<IWorkflowExternalControlService, WorkflowExternalControlService>();
        services.AddScoped<IRuntimeControlService, RuntimeControlService>();
        services.AddSingleton<IDevWorkerRuntimeRecoveryService, DevWorkerRuntimeRecoveryService>();
        services.AddScoped<IOperationRunReadAuthorizationService, OperationRunReadAuthorizationService>();
        services.AddScoped<IOperationRunCancelAuthorizationService, OperationRunCancelAuthorizationService>();
        services.AddScoped<IOperationDefinitionReadAuthorizationService, OperationDefinitionReadAuthorizationService>();
        services.AddScoped<IOperationDefinitionQueryService, OperationDefinitionQueryService>();
        services.AddScoped<IOperationCancelService, OperationCancelService>();
        services.AddScoped<IExecutionCancelService, ExecutionCancelService>();
        services.AddScoped<IOperationStartAuthorizationService, OperationStartAuthorizationService>();
        services.AddScoped<IOperationInputValidationService, OperationInputValidationService>();
        services.AddScoped<IRunKindResolver, RunKindResolver>();
        services.AddScoped<IRunQueryService, RunQueryService>();
        services.AddScoped<IRunActionAvailabilityQueryService, RunActionAvailabilityQueryService>();
        services.AddScoped<IRunTimelineQueryService, RunTimelineQueryService>();
        services.AddScoped<IOperationItemizationQueryService, OperationItemizationQueryService>();
        services.AddScoped<IOperationItemRetryService, OperationItemRetryService>();
        services.AddScoped<IModuleWorkflowListingService, ModuleWorkflowListingService>();
        services.AddScoped<IWorkflowStartAuthorizationService, WorkflowStartAuthorizationService>();
        services.AddScoped<IScheduleGovernanceService, ScheduleGovernanceService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IScheduleValidationService, ScheduleValidationService>();
        services.AddScoped<IScheduleCreateTimeZoneDefaults, ScheduleCreateTimeZoneDefaults>();
        services.AddScoped<IRecoveryGovernanceService, RecoveryGovernanceService>();
        services.AddScoped<IOperatorCapabilitiesQueryService, OperatorCapabilitiesQueryService>();
        services.AddScoped<IScheduleManagementService, SqlScheduleManagementService>();
        services.AddScoped<IControlActionAvailabilityProjection, ControlActionAvailabilityProjection>();
        services.AddScoped<IControlActivityQueryService, ControlActivityQueryService>();
        services.AddScoped<IControlAttentionQueryService, ControlAttentionQueryService>();
        services.AddScoped<IControlCenterQueryService, ControlCenterQueryService>();
        services.AddScoped<IDashboardCardProvider, RuntimeHealthDashboardCardProvider>();
        services.AddScoped<IDashboardCardProvider, BlockedWorkDashboardCardProvider>();
        services.AddScoped<IDashboardCardProvider, IntegrationsDashboardCardProvider>();
        services.AddScoped<IDashboardCardProvider, AttentionDashboardCardProvider>();
        services.AddScoped<IDashboardCardProvider, ExecutionsDashboardCardProvider>();
        services.AddScoped<IDashboardCardProvider, RecentActivityDashboardCardProvider>();
        services.AddScoped<IDashboardCardProvider, SchedulesDashboardCardProvider>();
        services.AddScoped<IDashboardCardProvider, AuditDashboardCardProvider>();
        services.AddScoped<IDashboardCardProvider, GovernanceDashboardCardProvider>();
        services.AddScoped<IDashboardCardProvider, AuthWarningsDashboardCardProvider>();
        services.AddScoped<IDashboardDefaultViewService, DashboardDefaultViewService>();
        services.AddScoped<IRuntimeModuleCompositionQueryService, RuntimeModuleCompositionQueryService>();
        services.AddScoped<IRuntimeHealthQueryService, RuntimeHealthQueryService>();
        services.AddScoped<ISchedulerStatusQueryService, SchedulerStatusQueryService>();
        services.AddScoped<IRecoveryQueryService, RecoveryQueryService>();
        services.AddScoped<IAuditTimelineQueryService, AuditTimelineQueryService>();
        services.AddHostedService<ModuleCompositionBootstrapHostedService>();
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
        foreach (var module in list)
            IntegrationConfigurationServiceCollectionExtensions.RegisterModuleIntegrationConfiguration(services, module);

        services.AddSingleton<IIntegrationConfigurationRegistry, IntegrationConfigurationRegistry>();

        services.AddSingleton<IModuleManifestProvider, KnownModuleManifestProvider>();
        return services;
    }
}
