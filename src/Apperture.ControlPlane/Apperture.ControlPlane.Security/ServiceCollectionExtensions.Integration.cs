using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
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
        services.AddScoped<IIntegrationRepository, SqlIntegrationRepository>();
        services.AddScoped<IIntegrationManagementService, IntegrationManagementService>();
        services.AddScoped<IIntegrationReadinessService, IntegrationReadinessService>();
        return services;
    }
}
