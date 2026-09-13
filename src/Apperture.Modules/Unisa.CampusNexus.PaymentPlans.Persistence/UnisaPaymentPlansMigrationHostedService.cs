using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Unisa.CampusNexus.PaymentPlans.Persistence;

internal sealed class UnisaPaymentPlansMigrationHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<UnisaPaymentPlansMigrationHostedService> _logger;

    public UnisaPaymentPlansMigrationHostedService(
        IServiceProvider services,
        ILogger<UnisaPaymentPlansMigrationHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UnisaPaymentPlansDbContext>();
        try
        {
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("UNISA payment plans module migrations applied successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "UNISA payment plans module database migration failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
