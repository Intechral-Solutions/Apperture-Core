using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Overlays the active <see cref="EntraInstanceRegistration"/> onto <see cref="EntraAuthOptions"/> so merger, validators, and OIDC see the same effective tenant.
/// </summary>
public sealed class EntraAuthOptionsDatabasePostConfigure : IPostConfigureOptions<EntraAuthOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EntraAuthOptionsDatabasePostConfigure> _logger;

    public EntraAuthOptionsDatabasePostConfigure(
        IServiceScopeFactory scopeFactory,
        ILogger<EntraAuthOptionsDatabasePostConfigure> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void PostConfigure(string? name, EntraAuthOptions options)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetService<IEntraInstanceRegistrationRepository>();
            if (repo is null)
                return;

            var active = repo.GetActiveAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (active is null || !active.Enabled)
                return;

            options.Authority = active.Authority;
            options.ClientId = active.ClientId;
            if (!string.IsNullOrWhiteSpace(active.AzureTenantId))
                options.AzureTenantId = active.AzureTenantId;
            options.AuthenticationContextId = string.IsNullOrWhiteSpace(active.AuthenticationContextId)
                ? null
                : active.AuthenticationContextId.Trim();
            if (!string.IsNullOrWhiteSpace(active.CallbackPath))
                options.CallbackPath = active.CallbackPath!;

            var resolver = scope.ServiceProvider.GetService<ISecretResolver>();
            var resolved = EntraClientSecretAccess
                .TryResolveAsync(active, resolver, options.ClientSecret, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (!string.IsNullOrWhiteSpace(resolved))
                options.ClientSecret = resolved;

            if (string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                _logger.LogWarning(
                    "Active Entra registration '{RegistrationId}' has no client secret in the secret store or Entra:ClientSecret. Entra sign-in stays disabled until a secret is recorded.",
                    active.Id);
                return;
            }

            options.Enabled = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not apply active Entra registration from the database; using configuration only.");
        }
    }
}
