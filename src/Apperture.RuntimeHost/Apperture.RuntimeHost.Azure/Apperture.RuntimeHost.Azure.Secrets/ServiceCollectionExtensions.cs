using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Providers;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.RuntimeHost.Azure.Secrets;

/// <summary>
/// Extension methods for configuring Azure Key Vault-backed secret resolution (Managed Identity).
/// Use in the Azure Functions host; do not register SQL-backed or bootstrap secret stores here.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Azure Key Vault-backed ISecretResolver and registry for the Azure runtime.
    /// Binds options from configuration section <see cref="AzureKeyVaultOptions.SectionName"/> ("KeyVault").
    /// Requires KeyVault:VaultUri when using kv: secret references.
    /// </summary>
    public static IServiceCollection AddAppertureAzureKeyVaultSecrets(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureKeyVaultOptions>(configuration.GetSection(AzureKeyVaultOptions.SectionName));
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureKeyVaultOptions>>().Value;
            if (options.VaultUri is null)
                throw new InvalidOperationException("KeyVault:VaultUri must be set when using AddAppertureAzureKeyVaultSecrets.");
            var credential = sp.GetRequiredService<TokenCredential>();
            return new SecretClient(options.VaultUri, credential);
        });
        services.AddScoped<AzureKeyVaultSecretResolver>();
        services.AddScoped<ISecretResolverRegistry, AzureSecretResolverRegistry>();
        services.AddScoped<CompositeSecretResolver>();
        services.AddScoped<ISecretResolver>(sp => sp.GetRequiredService<CompositeSecretResolver>());
        return services;
    }
}
