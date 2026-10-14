using Apperture.Bootstrap;
using Apperture.ControlPlane.Dashboard;
using Apperture.ControlPlane.Security.Audit;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.ComponentCatalog;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformSettings;
using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
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
        services.AddScoped<EntraClientSecretMaterialProvisioner>();
        services.AddScoped<IAuthenticationAdminOverviewService, AuthenticationAdminOverviewService>();
        services.AddScoped<IUserRepository, SqlUserRepository>();
        services.AddScoped<IEntraIdentityAssociationService, EntraIdentityAssociationService>();
        services.AddScoped<IEntraStepUpService, EntraStepUpService>();
        services.AddScoped<INativeTotpCredentialRepository, SqlNativeTotpCredentialRepository>();
        services.AddScoped<INativeMfaRecoveryCodeRepository, SqlNativeMfaRecoveryCodeRepository>();
        services.AddScoped<INativeMfaService, NativeMfaService>();
        services.AddScoped<IPendingMfaLoginTicketService, PendingMfaLoginTicketService>();
        services.AddScoped<IEmergencyAccessService, EmergencyAccessService>();
        services.AddHostedService<EmergencyAccessCanonicalizationHostedService>();
        services.AddScoped<IEntraPrimaryAuthenticationPolicy, EntraPrimaryAuthenticationPolicy>();
        services.AddScoped<IAuthenticationAssuranceEvaluator, AuthenticationAssuranceEvaluator>();
        services.AddScoped<IStrongAuthenticationGate, StrongAuthenticationGate>();
        services.AddScoped<IRecentAccountVerificationGate, RecentAccountVerificationGate>();
        services.AddScoped<IUserPreferencesRepository, SqlUserPreferencesRepository>();
        services.AddScoped<IUserProfileImageStore, SqlUserProfileImageStore>();
        services.AddSingleton<IProfileImageNormalizer, SkiaProfileImageNormalizer>();
        services.AddScoped<IAccountProfileService, AccountProfileService>();
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
        services.AddAppertureAuditRecorder();
        services.AddScoped<ILocalCredentialRepository, SqlLocalCredentialRepository>();
        services.AddSingleton<ILocalPasswordHasher, LocalPasswordHasher>();
        services.TryAddSingleton<IPasswordPolicyEvaluator, PasswordPolicyEvaluator>();
        services.AddScoped<IAccountActionTokenRepository, SqlAccountActionTokenRepository>();
        services.AddScoped<IAccountActionTokenService, AccountActionTokenService>();
        services.AddSingleton<AccountSecurityLinkBuilder>();
        services.AddSettingSecretResolution();
        services.TryAddScoped<IEmailSmtpCredentialResolver, EmailSmtpCredentialResolver>();
        services.AddScoped<IAccountSecurityMailSender, AccountSecurityMailSender>();
        services.AddScoped<IAccountContactService, AccountContactService>();
        services.AddScoped<IAccountInvitationService, AccountInvitationService>();
        services.AddScoped<IForgottenPasswordService, ForgottenPasswordService>();
        services.AddScoped<IFirstAdminBootstrapService, FirstAdminBootstrapService>();
        services.AddScoped<IEntraAccessRequestRepository, SqlEntraAccessRequestRepository>();
        services.AddScoped<IEntraProvisioningDirectoryService, EntraProvisioningDirectoryService>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<IEntraAccessRequestService, EntraAccessRequestService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IUserReferenceResolver, UserReferenceResolver>();
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
        services.AddScoped<IAuditedExecutionStartService, AuditedExecutionStartService>();
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
        services.AddScoped<IBulkOperationItemRetryService, BulkOperationItemRetryService>();
        services.AddScoped<IOperationItemAttentionService, OperationItemAttentionService>();
        services.AddScoped<ISharedRecordReferenceQueryService, SharedRecordReferenceQueryService>();
        services.AddScoped<DemoBusinessRecords.IDemoBusinessRecordProjectionService, DemoBusinessRecords.DemoBusinessRecordProjectionService>();
        services.AddScoped<IModuleWorkflowListingService, ModuleWorkflowListingService>();
        services.AddScoped<IWorkflowStartAuthorizationService, WorkflowStartAuthorizationService>();
        services.AddScoped<IScheduleGovernanceService, ScheduleGovernanceService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IScheduleValidationService, ScheduleValidationService>();
        services.AddScoped<IScheduleCreateTimeZoneDefaults, ScheduleCreateTimeZoneDefaults>();
        services.AddScoped<IRecoveryGovernanceService, RecoveryGovernanceService>();
        services.AddScoped<IOperatorCapabilitiesQueryService, OperatorCapabilitiesQueryService>();
        services.AddResourceContextPersistence();
        // Schedule creation resolves ownership against the owning Integration's ResourceContext.
        services.TryAddScoped<IIntegrationRepository, SqlIntegrationRepository>();
        services.AddScoped<IScheduleManagementService, SqlScheduleManagementService>();
        services.AddScoped<IControlActionAvailabilityProjection, ControlActionAvailabilityProjection>();
        services.AddScoped<IControlActivityQueryService, ControlActivityQueryService>();
        services.AddScoped<IInventoryQueryService, InventoryQueryService>();
        // Component catalog: a read-model projection over the authorized family read services. Two providers,
        // registered as a plain collection -- not a plugin framework, no persistence, no new permission.
        services.AddScoped<IComponentCatalogProvider, ConnectorComponentCatalogProvider>();
        services.AddScoped<IComponentCatalogProvider, NotificationDefinitionComponentCatalogProvider>();
        services.AddScoped<IComponentCatalog, ComponentCatalogService>();
        services.AddScoped<IControlAttentionQueryService, ControlAttentionQueryService>();
        services.AddScoped<IControlCenterQueryService, ControlCenterQueryService>();
        // Orphaned from the operator UI since NAV-1 (Control Center is the sole home); no
        // production caller remains. Kept registered undeleted pending a dedicated removal pass
        // rather than deleted blindly -- see DashboardController's doc comment and
        // docs/audits/nav1-architecture-review.md (F-01). The role/auth-posture query services
        // these providers wrap (IRoleProvider, IAuthenticationAdminOverviewService, etc.) are
        // independently useful and must not be removed alongside this.
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
    /// Identity, credential, audit, native MFA, and emergency-access services without hosted workers.
    /// For operator CLI. Requires AddApperturePersistence and an <see cref="ISecretProtector"/>.
    /// </summary>
    public static IServiceCollection AddAppertureEmergencyOperatorServices(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IUserRepository, SqlUserRepository>();
        services.AddScoped<IRoleRepository, SqlRoleRepository>();
        services.AddScoped<ILocalCredentialRepository, SqlLocalCredentialRepository>();
        services.AddScoped<IAuditService, SqlAuditService>();
        services.AddAppertureAuditRecorder();
        services.AddSingleton<ILocalPasswordHasher, LocalPasswordHasher>();
        services.AddScoped<INativeTotpCredentialRepository, SqlNativeTotpCredentialRepository>();
        services.AddScoped<INativeMfaRecoveryCodeRepository, SqlNativeMfaRecoveryCodeRepository>();
        services.AddScoped<INativeMfaService, NativeMfaService>();
        services.AddScoped<IEmergencyAccessService, EmergencyAccessService>();
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
