using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
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
    private readonly MigrationCompletionState? _migrations;

    /// <param name="migrations">
    /// Where the host records that its startup migrations are complete. A host that registers none has no such moment to compare
    /// with, and builds exactly as before.
    /// </param>
    public EntraAuthOptionsDatabasePostConfigure(
        IServiceScopeFactory scopeFactory,
        ILogger<EntraAuthOptionsDatabasePostConfigure> logger,
        IEntraOptionsBuildHealth? health = null,
        MigrationCompletionState? migrations = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _health = health;
        _migrations = migrations;
    }

    /// <summary>
    /// Records how the build went. The options are built once and kept, so a build made before the host's migrations completed read
    /// a registration store that may not have its schema or its rows yet, and what it reads is then frozen for the life of the
    /// process: whatever it found, it is recorded as degraded, never as a complete reading. Real traffic arrives after the
    /// migrations, so a correctly ordered host never records this.
    /// </summary>
    private void RecordBuild(EntraAuthOptions options, EntraOptionsBuildQuality quality, string? reasonCode = null)
    {
        if (_migrations is { IsComplete: false })
        {
            _health?.Record(options, EntraOptionsBuildQuality.Degraded, EntraOptionsBuildHealth.BuiltBeforeMigrationsComplete);
            return;
        }

        _health?.Record(options, quality, reasonCode);
    }

    public void PostConfigure(string? name, EntraAuthOptions options)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetService<IEntraInstanceRegistrationRepository>();
            if (repo is null)
            {
                RecordBuild(options, EntraOptionsBuildQuality.Complete);
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
                RecordBuild(options, EntraOptionsBuildQuality.Degraded, EntraOptionsBuildHealth.ClientSecretUnavailable);
            }
            else
            {
                RecordBuild(options, EntraOptionsBuildQuality.Complete);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not apply active Entra registration from the database; using configuration only.");
            RecordBuild(options, EntraOptionsBuildQuality.Degraded, EntraOptionsBuildHealth.RegistrationUnreadable);
        }
    }
}
