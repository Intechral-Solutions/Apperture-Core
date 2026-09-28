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
    /// Used by both Control Plane and Windows runtime; shared SQL store with host-appropriate protector (e.g. DPAPI on Windows).
    /// When <paramref name="protector"/> is null, registers DpapiSecretProtector on Windows.
    /// On non-Windows the default descriptor is fail-closed (resolving it throws
    /// PlatformNotSupportedException) but registration always completes so a later composition
    /// can replace it. A hosted startup validator then probes the final ISecretProtector
    /// so a real host without a supported protector cannot start. There is no insecure,
    /// environment-name, or OS-based working default. For Azure Linux, pass a custom protector
    /// or use the Azure Key Vault store.
    /// Requires AppertureDbContext and AddApperturePersistence to be registered first.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="protector">Optional. If null, uses DpapiSecretProtector on Windows; on non-Windows the default throws when resolved and host startup validation fails.</param>
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
            // Fail closed at resolve, not at registration. Throwing here would prevent a later
            // composition from replacing ISecretProtector after Program.cs registers the default.
            // SecretProtectorStartupValidationHostedService then fails a real host at startup if
            // the final composition still has this unsupported default.
            services.AddSingleton<ISecretProtector>(_ => throw CreateNonWindowsDpapiNotSupported());
        }

        services.AddScoped<ISecretAuditActorProvider>(_ => auditActorProvider ?? new DefaultSecretAuditActorProvider());
        services.AddScoped<SqlSecretStore>();
        services.AddScoped<ISecretStore>(sp => sp.GetRequiredService<SqlSecretStore>());
        services.AddScoped<SqlSecretResolver>();
        services.AddScoped<ISecretResolverRegistry, SqlSecretResolverRegistry>();
        services.AddScoped<CompositeSecretResolver>();
        services.AddScoped<ISecretResolver>(sp => sp.GetRequiredService<CompositeSecretResolver>());
        services.AddHostedService<SecretProtectorStartupValidationHostedService>();
        return services;
    }

    private static PlatformNotSupportedException CreateNonWindowsDpapiNotSupported() =>
        new(
            "Sql-backed secrets with DPAPI protector are supported only on Windows. " +
            "On non-Windows (e.g. Azure Linux), provide a custom ISecretProtector (e.g. certificate-backed) " +
            "or use the Azure Key Vault secret store instead.");
}
