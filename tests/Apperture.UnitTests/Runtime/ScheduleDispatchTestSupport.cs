using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.ModuleSdk.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.UnitTests.Runtime;

internal static class ScheduleDispatchTestSupport
{
    internal static IServiceCollection AddScheduleDispatchRunnerDependencies(
        this IServiceCollection services,
        IEnumerable<IModule>? modules = null)
    {
        services.AddScoped<IIntegrationDispatchReadinessEvaluator, AlwaysRunnableDispatchReadinessEvaluator>();
        if (!services.Any(d => d.ServiceType == typeof(IProcedureRegistryService)))
        {
            var moduleList = modules?.ToList() ?? [];
            services.AddSingleton<IProcedureRegistryService>(_ => new ProcedureRegistryService(moduleList, null));
        }

        return services;
    }

    private sealed class AlwaysRunnableDispatchReadinessEvaluator : IIntegrationDispatchReadinessEvaluator
    {
        public ValueTask<IntegrationReadinessProjection?> EvaluateForDispatchAsync(
            IntegrationId integrationId,
            string? procedureId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IntegrationReadinessProjection?>(new IntegrationReadinessProjection(
                integrationId.Value,
                "test.module",
                true,
                0,
                0,
                Array.Empty<IntegrationReadinessIssue>()));
    }
}
