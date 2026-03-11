using Apperture.Core.Application.Execution;
using Apperture.Core.Application.Services;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.RuntimePlane.Engine;

/// <summary>
/// DI registration for step execution and workflow executor.
/// Requires IRuntimeSlotResolver to be registered (e.g. via AddConnectorResolution).
/// </summary>
public static class StepExecutionServiceCollectionExtensions
{
    /// <summary>
    /// Registers step handler registry (singleton) and default workflow executor (scoped).
    /// </summary>
    public static IServiceCollection AddAppertureStepExecution(this IServiceCollection services)
    {
        services.AddSingleton<IStepHandlerRegistry, StepHandlerRegistry>();
        services.AddScoped<IWorkflowExecutor, DefaultWorkflowExecutor>();
        return services;
    }
}
