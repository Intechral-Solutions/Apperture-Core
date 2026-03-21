using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.ControlPlane.Security;

public sealed class RegistryBackedModuleActivationFilter : IModuleActivationFilter
{
    private readonly IServiceScopeFactory _scopeFactory;

    public RegistryBackedModuleActivationFilter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async ValueTask<bool> IsEnabledAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IModuleRegistryService>();
        var enabled = await registry.GetEnabledDescriptorsAsync(cancellationToken).ConfigureAwait(false);
        return enabled.Any(x => string.Equals(x.ModuleId, moduleId, StringComparison.Ordinal));
    }
}
