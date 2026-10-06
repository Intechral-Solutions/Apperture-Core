using Apperture.Persistence.SqlServer.Audit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Optional IHostedService that migrates the database on startup through <see cref="AppertureDatabaseMigrator"/>, then
/// normalizes historical actor values (<see cref="HistoricalActorNormalization"/>), then completes unclassified audit rows
/// (<see cref="AuditClassification"/>).
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
            await NormalizeHistoricalActorsAsync(db, cancellationToken).ConfigureAwait(false);
            await ClassifyAuditRowsAsync(db, cancellationToken).ConfigureAwait(false);
            _migrationCompletion.IsComplete = true;
            _logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            LogMigrationFailure(ex, db);
            throw;
        }
    }

    /// <summary>
    /// Runs on every start, after the schema is current, because an earlier binary (after a rollback) writes legacy actor
    /// values again. Unresolved and ambiguous values are results, not failures. An execution failure (connectivity, timeout,
    /// schema mismatch) means a required startup step did not complete, so it fails the start like a migration does: nothing
    /// is reported ready. Values already rewritten stay committed, and the next start resumes.
    /// </summary>
    private async Task NormalizeHistoricalActorsAsync(AppertureDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await HistoricalActorNormalization.RunAsync(db, _logger, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Historical actor normalization failed; the Management Host will not start. Correct the database or infrastructure problem and start it again. Values already normalized are kept and the next start resumes.");
            throw;
        }
    }

    /// <summary>
    /// Completes unclassified audit rows (AUD-1 design gate §15.6). Runs on every start, after the IAM-9.2a normalization and
    /// before readiness: the host reports not ready while it runs, and logs progress per batch. With nothing to classify it
    /// is one index seek. Unresolved and ambiguous actors are results; an execution failure fails the start like a
    /// migration, committed batches are kept, and the next start resumes.
    /// </summary>
    private async Task ClassifyAuditRowsAsync(AppertureDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await AuditClassification.RunAsync(db, _logger, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Audit record classification failed; the Management Host will not start. Correct the database or infrastructure problem and start it again. Rows already classified are kept and the next start resumes.");
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
