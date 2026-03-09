using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Extension methods for configuring operational secret management with SQL backing.
/// </summary>
public static class SecretsServiceCollectionExtensions
{
    /// <summary>
    /// Adds SQL-backed ISecretStore, ISecretResolver, and ISecretProtector (DPAPI) to the service collection.
    /// Requires AppertureDbContext and AddApperturePersistence to be registered first.
    /// </summary>
    public static IServiceCollection AddAppertureSqlSecrets(this IServiceCollection services)
    {
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddScoped<SqlSecretStore>();
        services.AddScoped<ISecretStore>(sp => sp.GetRequiredService<SqlSecretStore>());
        services.AddScoped<ISecretResolver, SqlSecretResolver>();
        return services;
    }
}
