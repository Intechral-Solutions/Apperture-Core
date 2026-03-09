using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.Bootstrap;

/// <summary>
/// Validates bootstrap SQL connection on application startup. Fails fast if connection is unavailable.
/// </summary>
internal sealed class BootstrapValidationHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BootstrapValidationHostedService> _logger;

    public BootstrapValidationHostedService(IServiceProvider services, ILogger<BootstrapValidationHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var profile = scope.ServiceProvider.GetRequiredService<IBootstrapConnectionProfile>();
        try
        {
            await profile.ValidateAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Bootstrap SQL connection validated successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Bootstrap SQL connection validation failed. Application cannot start.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
