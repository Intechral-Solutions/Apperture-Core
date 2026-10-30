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
/// <see cref="CreateAsync"/> returns <see langword="null"/> when no SQL Server endpoint is configured or
/// reachable and <c>APPERTURE_REQUIRE_SQL_TESTS</c> is not <c>1</c> (optional mode: the skip is announced through
/// <see cref="SqlTestExecution.NotExecuted"/>); with the variable set it throws instead, so it can never hand a caller a skip
/// to pass on. <see cref="CreateRequiredAsync"/> throws in both modes.
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
    /// suffix. When no SQL Server endpoint is reachable, returns <see langword="null"/> in optional mode and throws
    /// <see cref="SqlTestExecutionRequiredException"/> when <c>APPERTURE_REQUIRE_SQL_TESTS=1</c>.
    /// </summary>
    public static async Task<SqlServerScratchDatabase?> CreateAsync(string catalogPrefix)
    {
        var baseConnectionString = SqlTestExecution.ConnectionStringOrNotExecuted();
        if (baseConnectionString is null)
            return null;

        var catalog = catalogPrefix + Guid.NewGuid().ToString("N")[..12];

        try
        {
            var creationConnectionString =
                new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;
            SqlServerTestConfiguration.EnsureTestDatabaseExists(creationConnectionString);
        }
        catch (InvalidOperationException)
        {
            SqlTestExecution.NotExecuted("the test SQL Server could not be reached to create a scratch catalog");
            return null;
        }

        var scratch = new SqlServerScratchDatabase(baseConnectionString, catalog);

        await using (var db = scratch.NewDb())
        {
            if (!await db.Database.CanConnectAsync())
            {
                await scratch.DisposeAsync();
                SqlTestExecution.NotExecuted("the scratch catalog was created but cannot be connected to");
                return null;
            }

            await db.Database.MigrateAsync();
        }

        return scratch;
    }

    /// <summary>
    /// Like <see cref="CreateAsync"/>, but never soft-skips: without a reachable SQL Server it throws with configuration guidance
    /// (<see cref="SqlServerTestConfiguration.RequireConnectionString"/>, as <c>AuditSqlScratch</c> does), so a test that proves
    /// behavior of the server cannot pass without having run against one.
    /// </summary>
    public static async Task<SqlServerScratchDatabase> CreateRequiredAsync(string catalogPrefix)
    {
        // Throws with configuration guidance when no SQL Server is reachable.
        _ = SqlServerTestConfiguration.RequireConnectionString();
        var scratch = await CreateAsync(catalogPrefix);
        return scratch ?? throw new InvalidOperationException(
            "The SQL Server test catalog could not be created or migrated. " + SqlServerTestConfiguration.DescribeActiveConfiguration());
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
