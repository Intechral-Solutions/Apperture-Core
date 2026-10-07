using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Upgrades an already-designated emergency user in place to the canonical Recovery Access identity.
/// Does not revoke designation. Failures are logged so host startup cannot take down the recovery path.
/// </summary>
public sealed class EmergencyAccessCanonicalizationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmergencyAccessCanonicalizationHostedService> _logger;

    public EmergencyAccessCanonicalizationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<EmergencyAccessCanonicalizationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var emergency = scope.ServiceProvider.GetRequiredService<IEmergencyAccessService>();
            // The host applies this on its own, so the platform is the actor (not the emergency command line).
            await emergency.EnsureCanonicalIdentityAsync(AuditActor.System(), cancellationToken).ConfigureAwait(false);
        }
        catch (EmergencyAccessException ex) when (ex.Code == EmergencyAccessErrorCodes.UsernameConflict)
        {
            _logger.LogError(
                ex,
                "Emergency Access canonical identity could not be applied because another user already owns the reserved username. The existing designated recovery account was left unchanged.");
        }
        catch (AuditOutcomeUnknownException ex)
        {
            // Neither a commit nor a rollback was established (audit hardening design gate §13.2), so this must not say
            // the account was left unchanged. The rename and its audit row either both committed or neither did.
            _logger.LogError(
                ex,
                "Emergency Access canonical identity upgrade {OperationEventId} may or may not have been applied: its outcome could not be confirmed. Check the Recovery Access status before acting on it.",
                ex.OperationEventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Emergency Access canonical identity upgrade failed. The existing designated recovery account was left unchanged.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
