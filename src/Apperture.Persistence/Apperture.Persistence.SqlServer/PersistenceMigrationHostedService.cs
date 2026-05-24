using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Optional IHostedService that runs EF Core Migrate() on startup.
/// Register via AddApperturePersistenceMigrationsOnStartup when migrations should run automatically.
/// </summary>
internal sealed class PersistenceMigrationHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly MigrationCompletionState _migrationCompletion;
    private readonly ILogger<PersistenceMigrationHostedService> _logger;

    public PersistenceMigrationHostedService(
        IServiceProvider services,
        MigrationCompletionState migrationCompletion,
        ILogger<PersistenceMigrationHostedService> logger)
    {
        _services = services;
        _migrationCompletion = migrationCompletion;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        try
        {
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            _migrationCompletion.IsComplete = true;
            _logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Database migration failed. Application cannot start.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
