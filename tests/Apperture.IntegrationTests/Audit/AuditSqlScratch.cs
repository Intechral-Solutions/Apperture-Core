using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// A throwaway, fully migrated catalog for the AUD-1.1 SQL Server suites. Unlike the older soft-skipping helpers, this
/// one <b>fails</b> when no SQL Server is reachable (audit hardening design gate §22: new SQL suites fail rather than
/// soft-skip), so an absent server can never be reported as proof.
/// </summary>
public sealed class AuditSqlScratch : IAsyncDisposable
{
    private readonly string _baseConnectionString;

    private AuditSqlScratch(string baseConnectionString, string catalog)
    {
        _baseConnectionString = baseConnectionString;
        Catalog = catalog;
        ConnectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;
    }

    public string Catalog { get; }
    public string ConnectionString { get; }

    /// <summary>The marker every test writes to its output once it has run against SQL Server.</summary>
    public string ExecutionMarker => $"EXECUTED against SQL Server catalog {Catalog}";

    public static async Task<AuditSqlScratch> CreateAsync(string prefix)
    {
        // Throws with configuration guidance when no SQL Server is reachable.
        var baseConnectionString = SqlServerTestConfiguration.RequireConnectionString();
        var scratch = new AuditSqlScratch(baseConnectionString, prefix + Guid.NewGuid().ToString("N")[..12]);
        SqlServerTestConfiguration.EnsureTestDatabaseExists(scratch.ConnectionString);
        await using (var db = scratch.NewDb())
            await AppertureDatabaseMigrator.MigrateAsync(db);
        return scratch;
    }

    /// <summary>
    /// A throwaway catalog migrated only as far as <paramref name="migrationId"/>, the state of an existing installation before the
    /// next migration. Seed it with the old schema's SQL, then call <see cref="MigrateToCurrentAsync"/>. Fails, never skips,
    /// without SQL Server.
    /// </summary>
    public static async Task<AuditSqlScratch> CreateAtMigrationAsync(string prefix, string migrationId)
    {
        var baseConnectionString = SqlServerTestConfiguration.RequireConnectionString();
        var scratch = new AuditSqlScratch(baseConnectionString, prefix + Guid.NewGuid().ToString("N")[..12]);
        SqlServerTestConfiguration.EnsureTestDatabaseExists(scratch.ConnectionString);
        await using var db = scratch.NewDb();
        await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(migrationId);
        return scratch;
    }

    /// <summary>Applies every pending migration through the supported entry point, as a Management Host start does.</summary>
    public async Task MigrateToCurrentAsync()
    {
        await using var db = NewDb();
        await AppertureDatabaseMigrator.MigrateAsync(db);
    }

    /// <summary>Moves the schema to <paramref name="migrationId"/>, which may be behind the current one (a rollback).</summary>
    public async Task MigrateToAsync(string migrationId)
    {
        await using var db = NewDb();
        await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(migrationId);
    }

    /// <summary>A context configured as production configures it: retry on failure and the recorder-row guard.</summary>
    public AppertureDbContext NewDb(params IInterceptor[] interceptors) => new(Options(interceptors));

    public DbContextOptions<AppertureDbContext> Options(params IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure())
            .AddInterceptors([AuditRecorderRowGuardInterceptor.Instance, .. interceptors])
            .Options;

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is DBNull or null ? default! : (T)value;
    }

    public async Task<List<Dictionary<string, object?>>> RowsAsync(string sql)
    {
        var rows = new List<Dictionary<string, object?>>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Inserts a user with a current ledger row and a Local link, as the username foundation requires.</summary>
    public Task AddUserAsync(string id, string username) =>
        ExecuteAsync($"""
            INSERT INTO Users (Id, Username, DisplayName, IsBreakGlassEligible) VALUES (N'{id}', N'{username}', N'{id}', 0);
            INSERT INTO UsernameLedger (Username, UserId, AssignedAtUtc) VALUES (N'{username}', N'{id}', SYSDATETIMEOFFSET());
            INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, LinkedAt) VALUES (N'L{id}', N'{id}', 1, N'{username}', SYSDATETIMEOFFSET());
            """);

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        try
        {
            await using var connection = new SqlConnection(
                new SqlConnectionStringBuilder(_baseConnectionString) { InitialCatalog = "master" }.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{Catalog}')
                BEGIN
                    ALTER DATABASE [{Catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Catalog}];
                END
                """;
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            // Cleanup is best-effort; a leftover throwaway catalog must not fail the run.
        }
    }
}
