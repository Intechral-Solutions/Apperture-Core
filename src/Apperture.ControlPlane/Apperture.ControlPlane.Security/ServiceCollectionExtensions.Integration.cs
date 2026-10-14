using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Registers integration workflow services. Epic 49.
/// Requires AddAppertureGovernance and AddApperturePersistence (DbContext) to be registered.
/// </summary>
public static class IntegrationServiceCollectionExtensions
{
    /// <summary>
    /// Adds integration management workflows.
    /// </summary>
    public static IServiceCollection AddAppertureIntegrationWorkflows(this IServiceCollection services)
    {
        services.AddResourceContextPersistence();
        services.AddScoped<IIntegrationRepository, SqlIntegrationRepository>();
        services.AddScoped<IAttentionIncidentTracker, SqlAttentionIncidentTracker>();
        services.AddScoped<IAttentionIncidentAcknowledgementService, AttentionIncidentAcknowledgementService>();
        services.AddScoped<IAttentionRecipientResolver, SqlAttentionRecipientResolver>();
        services.AddScoped<IAttentionRoutingManagementService, AttentionRoutingManagementService>();
        services.AddScoped<IAttentionContactGroupManagementService, AttentionContactGroupManagementService>();
        services.AddScoped<IOperationalTeamManagementService, OperationalTeamManagementService>();
        services.AddScoped<IIntegrationManagementService, IntegrationManagementService>();
        services.AddScoped<IResourceContextManagementService, ResourceContextManagementService>();
        services.AddScoped<IResourceOwnershipMoveService, ResourceOwnershipMoveService>();
        services.AddScoped<IResourceOwnershipMutationGate, SqlResourceOwnershipMutationGate>();
        services.AddSingleton<IScheduleExpectationEvaluator, ScheduleExpectationEvaluator>();
        services.AddScoped<IIntegrationScheduleDiagnosticsService, IntegrationScheduleDiagnosticsService>();
        services.AddIntegrationDispatchReadiness();
        services.AddScoped<IIntegrationReadinessService, IntegrationReadinessService>();
        services.AddScoped<IIntegrationProcedureService, IntegrationProcedureService>();
        return services;
    }

    /// <summary>
    /// Epic 68: binds <see cref="AttentionIncidentObservationOptions"/> and registers the background observer.
    /// </summary>
    public static IServiceCollection AddAttentionIncidentObservation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AttentionIncidentObservationOptions>(
            configuration.GetSection(AttentionIncidentObservationOptions.SectionName));
        services.AddHostedService<AttentionIncidentObservationHostedService>();
        return services;
    }
}

