using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proof.CampusNexus.StudentReview.Persistence;

internal sealed class ProofStudentReviewMigrationHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ProofStudentReviewMigrationHostedService> _logger;

    public ProofStudentReviewMigrationHostedService(
        IServiceProvider services,
        ILogger<ProofStudentReviewMigrationHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProofStudentReviewDbContext>();
        try
        {
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Proof CampusNexus StudentReview module migrations applied successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Proof StudentReview module database migration failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
