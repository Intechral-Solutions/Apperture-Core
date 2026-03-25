using Apperture.Core.Application.Services;
using Apperture.Core.Application.Integration;
using Apperture.Connectors.Sftp;
using Apperture.Persistence.SqlServer;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Registers connector workflow services: connection management, binding, eligibility, slot registry.
/// Requires AddAppertureGovernance and AddApperturePersistence (DbContext) to be registered.
/// </summary>
public static class ConnectorWorkflowServiceCollectionExtensions
{
    /// <summary>
    /// Adds connector management workflows: connections, bindings, eligibility.
    /// </summary>
    public static IServiceCollection AddAppertureConnectorWorkflows(this IServiceCollection services)
    {
        services.AddScoped<IConnectionRepository, SqlConnectionRepository>();
        services.AddScoped<IBindingRepository, SqlBindingRepository>();
        services.AddSingleton<IModuleSlotRegistry, StubModuleSlotRegistry>();
        services.AddScoped<IBindingEligibilityService, BindingEligibilityService>();
        services.AddScoped<IConnectionManagementService, ConnectionManagementService>();
        services.AddScoped<IConnectionVerificationService, ConnectionVerificationService>();
        services.AddScoped<SftpConnectorClientFactory>();
        services.AddScoped<IConnectionVerifier, SftpConnectionVerifier>();
        services.AddScoped<IConnectionVerificationReadinessPolicy, AdvisoryConnectionVerificationReadinessPolicy>();
        services.AddScoped<IBindingService, BindingService>();
        services.AddScoped<IConnectorDefinitionCatalogReadService, ConnectorDefinitionCatalogReadService>();
        return services;
    }
}
