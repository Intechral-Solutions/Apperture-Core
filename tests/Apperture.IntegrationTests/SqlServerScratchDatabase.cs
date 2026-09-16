using Apperture.Persistence.SqlServer;
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
/// reachable; callers soft-skip in that case rather than failing the run in environments without SQL
/// Server (see <see cref="SqlServerTestConfiguration"/>).
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
    /// suffix. Returns <see langword="null"/> (soft-skip) when no SQL Server endpoint is reachable.
    /// </summary>
    public static async Task<SqlServerScratchDatabase?> CreateAsync(string catalogPrefix)
    {
        var baseConnectionString = SqlServerTestConfiguration.GetConnectionString();
        if (baseConnectionString is null)
            return null; // soft-skip: no reachable SQL endpoint configured

        var catalog = catalogPrefix + Guid.NewGuid().ToString("N")[..12];

        try
        {
            var creationConnectionString =
                new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;
            SqlServerTestConfiguration.EnsureTestDatabaseExists(creationConnectionString);
        }
        catch (InvalidOperationException)
        {
            return null; // soft-skip: endpoint unreachable
        }

        var scratch = new SqlServerScratchDatabase(baseConnectionString, catalog);

        await using (var db = scratch.NewDb())
        {
            if (!await db.Database.CanConnectAsync())
            {
                await scratch.DisposeAsync();
                return null; // soft-skip
            }

            await db.Database.MigrateAsync();
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
