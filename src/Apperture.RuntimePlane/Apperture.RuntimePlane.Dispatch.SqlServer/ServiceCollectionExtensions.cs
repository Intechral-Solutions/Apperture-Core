using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.RuntimePlane.Dispatch.SqlServer;

/// <summary>
/// Registers SQL-backed workflow/operation start dispatch used by WorkerHost and Management.Host.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IWorkflowPersistence"/>, start dispatch services, and <see cref="ISchedulerLifecycleGuard"/>.
    /// Call after <c>AddApperturePersistence</c> and runtime composition (workflow/operation registries).
    /// </summary>
    public static IServiceCollection AddSqlWorkflowDispatchServices(this IServiceCollection services)
    {
        services.AddScoped<IWorkflowPersistence, SqlWorkflowPersistence>();
        services.AddScoped<IWorkflowStartDispatchService, WorkflowStartDispatchService>();
        services.AddScoped<IOperationStartDispatchService, OperationStartDispatchService>();
        services.AddScoped<ISchedulerLifecycleGuard, SchedulerLifecycleGuard>();
        return services;
    }

    /// <summary>
    /// Registers scheduler dispatch runner for runtime hosts. Epic 28 (workflow), Epic 29 (operation), Epic 36 (multi-host).
    /// Requires <see cref="AddSqlWorkflowDispatchServices"/> (or equivalent registrations) and schedule persistence.
    /// Optionally register <see cref="ISchedulerHostIdentity"/> and <see cref="ISchedulerClaimOptions"/> before calling to override defaults.
    /// </summary>
    public static IServiceCollection AddRuntimeSchedulerDispatch(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(ISchedulerHostIdentity)))
            services.AddSingleton<ISchedulerHostIdentity>(new DefaultSchedulerHostIdentity());
        if (!services.Any(d => d.ServiceType == typeof(ISchedulerClaimOptions)))
            services.AddSingleton<ISchedulerClaimOptions>(new DefaultSchedulerClaimOptions());
        services.AddScoped<IScheduleDispatchRunner, ScheduleDispatchRunner>();
        return services;
    }
}
