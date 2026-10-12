using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// A throwaway migrated catalog on either isolation mode: the default locking READ COMMITTED, or READ_COMMITTED_SNAPSHOT. The
/// audit table is append-only, so a case that asserts on audit rows takes a catalog of its own. A suite that proves lock behaviour
/// runs each case on both modes, since a reader behaves differently under row versioning while a writer's <c>UPDLOCK</c> does
/// not. Fails, never skips, without SQL Server (<see cref="AuditSqlScratch"/>).
/// </summary>
internal static class AuditIsolation
{
    public static async Task<AuditSqlScratch> CreateAsync(string prefix, bool rcsi)
    {
        var scratch = await AuditSqlScratch.CreateAsync(prefix + (rcsi ? "B_" : "A_"));
        if (!rcsi)
        {
            (await scratch.ScalarAsync<bool>(RcsiQuery(scratch))).Should().BeFalse();
            return scratch;
        }

        await using (var connection = new SqlConnection(new SqlConnectionStringBuilder(scratch.ConnectionString) { InitialCatalog = "master" }.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{scratch.Catalog}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;";
            await command.ExecuteNonQueryAsync();
        }

        // The switch closed this catalog's idle pooled connections; drop that pool so none is reused half-dead.
        SqlConnection.ClearPool(new SqlConnection(scratch.ConnectionString));
        (await scratch.ScalarAsync<bool>(RcsiQuery(scratch))).Should().BeTrue("the catalog must really run under row versioning");
        return scratch;
    }

    private static string RcsiQuery(AuditSqlScratch scratch) =>
        $"SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'{scratch.Catalog}';";
}
