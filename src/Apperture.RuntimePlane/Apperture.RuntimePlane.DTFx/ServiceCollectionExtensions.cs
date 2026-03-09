using Apperture.RuntimePlane.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.RuntimePlane.DTFx;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppertureDTFx(this IServiceCollection services)
    {
        services.AddSingleton<IDurableTaskRuntimeAdapter, DurableTaskRuntimeAdapterStub>();
        return services;
    }
}

public interface IDurableTaskRuntimeAdapter { }

internal sealed class DurableTaskRuntimeAdapterStub : IDurableTaskRuntimeAdapter { }
