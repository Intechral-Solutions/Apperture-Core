using System.Runtime.InteropServices;
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
    /// Adds SQL-backed ISecretStore, ISecretResolver, and ISecretProtector to the service collection.
    /// When <paramref name="protector"/> is null, registers DpapiSecretProtector on Windows only;
    /// on non-Windows, throws. For Azure Linux, provide a custom protector or use Azure Key Vault store.
    /// Requires AppertureDbContext and AddApperturePersistence to be registered first.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="protector">Optional. If null, uses DpapiSecretProtector on Windows; fails on non-Windows.</param>
    /// <param name="auditActorProvider">Optional. If null, uses DefaultSecretAuditActorProvider ("system"). Provide custom to pass authenticated user/service identity.</param>
    public static IServiceCollection AddAppertureSqlSecrets(
        this IServiceCollection services,
        ISecretProtector? protector = null,
        ISecretAuditActorProvider? auditActorProvider = null)
    {
        if (protector is not null)
        {
            services.AddSingleton<ISecretProtector>(protector);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        }
        else
        {
            throw new PlatformNotSupportedException(
                "Sql-backed secrets with DPAPI protector are supported only on Windows. " +
                "On non-Windows (e.g. Azure Linux), provide a custom ISecretProtector (e.g. certificate-backed) " +
                "or use the Azure Key Vault secret store instead.");
        }

        services.AddScoped<ISecretAuditActorProvider>(_ => auditActorProvider ?? new DefaultSecretAuditActorProvider());
        services.AddScoped<SqlSecretStore>();
        services.AddScoped<ISecretStore>(sp => sp.GetRequiredService<SqlSecretStore>());
        services.AddScoped<SqlSecretResolver>();
        services.AddScoped<ISecretResolverRegistry, SqlSecretResolverRegistry>();
        services.AddScoped<CompositeSecretResolver>();
        services.AddScoped<ISecretResolver>(sp => sp.GetRequiredService<CompositeSecretResolver>());
        return services;
    }
}
