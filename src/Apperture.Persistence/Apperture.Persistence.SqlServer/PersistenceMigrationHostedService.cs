using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Optional IHostedService that migrates the database on startup through <see cref="AppertureDatabaseMigrator"/>.
/// Register via AddApperturePersistenceMigrationsOnStartup when migrations should run automatically.
/// </summary>
internal sealed class PersistenceMigrationHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly MigrationCompletionState _migrationCompletion;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PersistenceMigrationHostedService> _logger;

    public PersistenceMigrationHostedService(
        IServiceProvider services,
        MigrationCompletionState migrationCompletion,
        IHostEnvironment environment,
        ILogger<PersistenceMigrationHostedService> logger)
    {
        _services = services;
        _migrationCompletion = migrationCompletion;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (pending.Count > 0)
            {
                _logger.LogInformation(
                    "Applying {PendingCount} pending database migration(s). First={FirstMigration}, Last={LastMigration}",
                    pending.Count,
                    pending[0],
                    pending[^1]);
            }

            // Includes the username backfill data step; readiness is reported only after all of it.
            await AppertureDatabaseMigrator.MigrateAsync(db, _logger, cancellationToken).ConfigureAwait(false);
            _migrationCompletion.IsComplete = true;
            _logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            LogMigrationFailure(ex, db);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void LogMigrationFailure(Exception ex, AppertureDbContext db)
    {
        var sql = FindSqlException(ex);
        string? databaseName = null;
        string? dataSource = null;
        try
        {
            var connection = db.Database.GetDbConnection();
            databaseName = connection.Database;
            dataSource = connection.DataSource;
        }
        catch (Exception connectionEx)
        {
            _logger.LogDebug(connectionEx, "Could not read DbConnection metadata during migration failure logging.");
        }

        if (sql is not null)
        {
            // Number/State/Class are safe and essential for diagnosis; include them in the message
            // so Aspire/dashboard views that truncate exception detail still surface the SQL error.
            _logger.LogCritical(
                ex,
                "Database migration failed. Application cannot start. SqlError Number={SqlNumber}, State={SqlState}, Class={SqlClass}, Server={SqlServer}, ClientConnectionId={ClientConnectionId}, Database={Database}, DataSource={DataSource}. Message={SqlMessage}",
                sql.Number,
                sql.State,
                sql.Class,
                sql.Server,
                sql.ClientConnectionId,
                databaseName,
                dataSource,
                sql.Message);

            if (_environment.IsDevelopment())
            {
                foreach (SqlError error in sql.Errors)
                {
                    _logger.LogCritical(
                        "SQL Server error detail: Number={SqlNumber}, State={SqlState}, Class={SqlClass}, Procedure={Procedure}, Line={LineNumber}, Message={SqlMessage}",
                        error.Number,
                        error.State,
                        error.Class,
                        error.Procedure,
                        error.LineNumber,
                        error.Message);
                }
            }

            return;
        }

        _logger.LogCritical(
            ex,
            "Database migration failed. Application cannot start. Database={Database}, DataSource={DataSource}",
            databaseName,
            dataSource);
    }

    private static SqlException? FindSqlException(Exception? ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql)
                return sql;
        }

        return null;
    }
}
