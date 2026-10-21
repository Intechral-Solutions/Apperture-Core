using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Overlays the active <see cref="EntraInstanceRegistration"/> onto <see cref="EntraAuthOptions"/> so merger, validators, and OIDC see the same effective tenant.
/// </summary>
/// <remarks>
/// <para>The read of the registration and its secret goes through a scope and a connection of its own and is made the first time the
/// options are asked for, so it must not be the first thing a transaction that has written a registration row does: under locking
/// READ COMMITTED it would wait for that row, and under READ_COMMITTED_SNAPSHOT it would read the row as it was before the
/// transaction. A writer of registrations therefore builds the options before it opens its unit (see
/// <see cref="AdministratorViability.IAdministratorViabilityCoordinator.PrepareAsync"/>).</para>
/// <para>A build that cannot read the registration or its secret still leaves the deployment configuration in place and still does
/// not throw, as it always has; it records that it was degraded (<see cref="IEntraOptionsBuildHealth"/>) so that nothing downstream
/// takes the cached options for a complete reading.</para>
/// </remarks>
public sealed class EntraAuthOptionsDatabasePostConfigure : IPostConfigureOptions<EntraAuthOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EntraAuthOptionsDatabasePostConfigure> _logger;
    private readonly IEntraOptionsBuildHealth? _health;

    public EntraAuthOptionsDatabasePostConfigure(
        IServiceScopeFactory scopeFactory,
        ILogger<EntraAuthOptionsDatabasePostConfigure> logger,
        IEntraOptionsBuildHealth? health = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _health = health;
    }

    public void PostConfigure(string? name, EntraAuthOptions options)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetService<IEntraInstanceRegistrationRepository>();
            if (repo is null)
            {
                _health?.Record(options, EntraOptionsBuildQuality.Complete);
                return;
            }

            var active = repo.GetActiveAsync(CancellationToken.None).GetAwaiter().GetResult();

            var resolver = active is { Enabled: true } ? scope.ServiceProvider.GetService<ISecretResolver>() : null;
            var result = EntraAuthOptionsOverlay
                .ApplyDetailedAsync(active, options, resolver, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();

            if (result.Outcome == EntraOverlayOutcome.AppliedWithoutSecret)
            {
                _logger.LogWarning(
                    "Active Entra registration '{RegistrationId}' has no client secret in the secret store or Entra:ClientSecret. Entra sign-in stays disabled until a secret is recorded.",
                    active!.Id);
            }

            if (result.SecretStatus == EntraClientSecretStatus.Unavailable)
            {
                _logger.LogWarning(
                    "The client secret of the active Entra registration '{RegistrationId}' could not be read from the secret store. The deployment secret stands in where the registration asks for it; the host's reading of its own sign-in is incomplete until it restarts.",
                    active!.Id);
                _health?.Record(options, EntraOptionsBuildQuality.Degraded, EntraOptionsBuildHealth.ClientSecretUnavailable);
            }
            else
            {
                _health?.Record(options, EntraOptionsBuildQuality.Complete);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not apply active Entra registration from the database; using configuration only.");
            _health?.Record(options, EntraOptionsBuildQuality.Degraded, EntraOptionsBuildHealth.RegistrationUnreadable);
        }
    }
}
