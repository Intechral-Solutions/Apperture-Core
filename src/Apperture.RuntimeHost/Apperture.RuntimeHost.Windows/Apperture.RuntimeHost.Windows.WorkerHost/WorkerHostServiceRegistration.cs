using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

public static class WorkerHostServiceRegistration
{
    public static IServiceCollection AddWorkerHostIntegrationRepository(this IServiceCollection services)
    {
        // Worker scheduler dispatch requires integration state to decide whether scheduled work is allowed.
        // This mapping is intentionally registered in WorkerHost (not only in ControlPlane hosts).
        services.AddScoped<IIntegrationRepository, SqlIntegrationRepository>();
        return services;
    }
}

