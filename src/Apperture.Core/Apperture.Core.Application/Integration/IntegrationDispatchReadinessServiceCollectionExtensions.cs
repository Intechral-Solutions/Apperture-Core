using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Core.Application.Integration;

/// <summary>Registers SDK-6C dispatch readiness evaluation for runtime hosts.</summary>
public static class IntegrationDispatchReadinessServiceCollectionExtensions
{
    public static IServiceCollection AddIntegrationDispatchReadiness(this IServiceCollection services)
    {
        services.AddScoped<IntegrationConfigurationEvidenceBuilder>();
        services.AddScoped<IntegrationDispatchReadinessEvaluator>();
        services.AddScoped<IIntegrationDispatchReadinessEvaluator>(sp =>
            sp.GetRequiredService<IntegrationDispatchReadinessEvaluator>());
        return services;
    }

    /// <summary>
    /// Baseline verification policy for worker/runtime hosts when control-plane connector workflows are not registered.
    /// </summary>
    public static IServiceCollection AddAdvisoryConnectionVerificationReadinessPolicy(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(IConnectionVerificationReadinessPolicy)))
            services.AddScoped<IConnectionVerificationReadinessPolicy, AdvisoryConnectionVerificationReadinessPolicy>();
        return services;
    }
}

/// <summary>Epic 59 baseline: verification issues are explicit but non-blocking.</summary>
public sealed class AdvisoryConnectionVerificationReadinessPolicy : IConnectionVerificationReadinessPolicy
{
    public IntegrationReadinessIssueSeverity ResolveSeverity(
        string moduleId,
        string slotName,
        ConnectionVerificationStatus verificationStatus) =>
        IntegrationReadinessIssueSeverity.Advisory;
}
