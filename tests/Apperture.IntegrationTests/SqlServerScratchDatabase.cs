using Apperture.Persistence.SqlServer;
using Apperture.SqlTestTargets;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Apperture.IntegrationTests;

/// <summary>
/// A throwaway per-test SQL Server catalog: created and migrated on <see cref="CreateAsync"/>, torn down
/// best-effort on dispose. Every SQL Server integration test in the CTX-3B Plan 2/4 series uses this so
/// the create/migrate/drop lifecycle is written exactly once, and so tests remain safe to run in parallel
/// — each test gets its own catalog rather than sharing the integration database.
/// </summary>
/// <remarks>
/// <see cref="CreateAsync"/> never hands a caller a skip to pass on: without a reachable SQL Server, or when the catalog cannot
/// be created, connected to or migrated, it throws (<see cref="SqlTestUnavailableException"/> for unavailability, the original
/// exception for a migration failure, <c>SqlTestTargetRefusedException</c> for a refused target) and drops the catalog it made.
/// </remarks>
public sealed class SqlServerScratchDatabase : IAsyncDisposable
{
    private readonly string _baseConnectionString;
    private readonly string _catalog;

    private SqlServerScratchDatabase(string baseConnectionString, string catalog)
    {
        _baseConnectionString = baseConnectionString;
        _catalog = catalog;
    }

    /// <summary>
    /// Creates and migrates a new throwaway catalog named <paramref name="catalogPrefix"/> plus a random
    /// suffix. Throws when SQL Server cannot be used; it never returns null and never skips.
    /// </summary>
    public static async Task<SqlServerScratchDatabase> CreateAsync(string catalogPrefix)
    {
        // Throws SqlTestUnavailableException with configuration guidance when no SQL Server is reachable.
        var baseConnectionString = SqlServerTestConfiguration.RequireConnectionString();

        var catalog = catalogPrefix + Guid.NewGuid().ToString("N")[..12];
        var creationConnectionString =
            new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;
        SqlServerTestConfiguration.EnsureTestDatabaseExists(creationConnectionString);

        var scratch = new SqlServerScratchDatabase(baseConnectionString, catalog);
        try
        {
            await using var db = scratch.NewDb();
            if (!await db.Database.CanConnectAsync())
                throw SqlTestExecution.Unavailable("the scratch catalog was created but cannot be connected to");

            await db.Database.MigrateAsync();
        }
        catch
        {
            await scratch.DisposeAsync();
            throw;
        }

        return scratch;
    }

    /// <summary>Opens a fresh <see cref="AppertureDbContext"/> against this scratch catalog.</summary>
    public AppertureDbContext NewDb()
    {
        var connectionString = new SqlConnectionStringBuilder(_baseConnectionString) { InitialCatalog = _catalog }.ConnectionString;
        return new AppertureDbContext(new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(connectionString)
            .Options);
    }

    /// <summary>Best-effort drop of the scratch catalog. A leftover throwaway catalog must not fail a test run.</summary>
    public async ValueTask DisposeAsync()
    {
        // The catalog was validated when it was created; a drop is validated again, outside the best-effort catch below.
        SqlTestTargetGuard.EnsureDisposableCatalogOnServer(_baseConnectionString, _catalog, "drop a test catalog");

        try
        {
            var master = new SqlConnectionStringBuilder(_baseConnectionString) { InitialCatalog = "master" }.ConnectionString;
            await using var conn = new SqlConnection(master);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{_catalog}')
                BEGIN
                    ALTER DATABASE [{_catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_catalog}];
                END
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Cleanup is best-effort; a leftover throwaway catalog must not fail the run.
        }
    }
}
