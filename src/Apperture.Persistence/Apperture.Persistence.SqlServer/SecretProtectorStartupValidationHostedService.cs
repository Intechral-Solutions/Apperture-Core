using Apperture.Core.Security.Secrets.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Resolves the final <see cref="ISecretProtector"/> after all service replacements and fails host
/// startup if it is missing or cannot protect/unprotect. Registration of the default non-Windows
/// descriptor must not throw (so a later composition can replace it); this is the fail-fast boundary.
/// </summary>
internal sealed class SecretProtectorStartupValidationHostedService : IHostedService
{
    private static readonly byte[] Probe = [0xA9, 0x70, 0x3E];
    private const string ProbeKeyId = "apperture.sql-secrets.startup";

    private readonly IServiceProvider _services;
    private readonly ILogger<SecretProtectorStartupValidationHostedService> _logger;

    public SecretProtectorStartupValidationHostedService(
        IServiceProvider services,
        ILogger<SecretProtectorStartupValidationHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var protector = _services.GetRequiredService<ISecretProtector>();
            var protectedPayload = await protector.ProtectAsync(Probe, ProbeKeyId, cancellationToken).ConfigureAwait(false);
            var roundTrip = await protector.UnprotectAsync(protectedPayload, ProbeKeyId, cancellationToken).ConfigureAwait(false);
            if (!roundTrip.AsSpan().SequenceEqual(Probe))
            {
                throw new InvalidOperationException(
                    "ISecretProtector startup round-trip did not return the original payload. The SQL-backed secret store cannot start.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "SQL secret protector validation failed. Application cannot start.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
