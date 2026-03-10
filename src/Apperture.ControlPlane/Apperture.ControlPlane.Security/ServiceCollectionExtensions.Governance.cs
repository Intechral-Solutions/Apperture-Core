using Apperture.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Registers governance services for Phase 1 scaffolding: registry, stub auth, stub principal context.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds platform authorization and resource governance services (Phase 1 scaffolding).
    /// </summary>
    public static IServiceCollection AddAppertureGovernance(this IServiceCollection services)
    {
        services.AddSingleton<IResourceDescriptorRegistry>(_ =>
        {
            var registry = new InMemoryResourceDescriptorRegistry();
            PlatformPermissionSeeder.Seed(registry);
            return registry;
        });
        services.AddScoped<IAuthorizationService, StubAuthorizationService>();
        services.AddScoped<IPrincipalContext, StubPrincipalContext>();
        return services;
    }
}
