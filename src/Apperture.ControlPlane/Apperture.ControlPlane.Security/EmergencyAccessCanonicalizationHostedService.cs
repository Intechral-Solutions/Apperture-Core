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
            await emergency.EnsureCanonicalIdentityAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (EmergencyAccessException ex) when (ex.Code == EmergencyAccessErrorCodes.UsernameConflict)
        {
            _logger.LogError(
                ex,
                "Emergency Access canonical identity could not be applied because another user already owns the reserved username. The existing designated recovery account was left unchanged.");
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
