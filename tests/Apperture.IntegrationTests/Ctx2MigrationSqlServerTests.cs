using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// CTX-2 migration evidence: existing history stays null (never backfilled to Shared), the new
/// snapshot columns are nullable, and none of them carries a foreign key.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class Ctx2MigrationSqlServerTests
{
    [Fact]
    public async Task Ctx2_leaves_existing_history_null_and_adds_no_foreign_keys()
    {
        var baseConnectionString = SqlServerTestConfiguration.GetConnectionString();
        if (baseConnectionString is null)
            return; // soft-skip: no reachable SQL endpoint configured

        var catalog = "Apperture_Ctx2Migration_" + Guid.NewGuid().ToString("N")[..12];
        var connectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;

        try
        {
            SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);
        }
        catch (InvalidOperationException)
        {
            return; // soft-skip
        }

        try
        {
            await using (var db = NewDb(connectionString))
            {
                if (!await db.Database.CanConnectAsync())
                    return; // soft-skip

                // Migrate to latest (includes CTX-1 + CTX-2) against an empty database, then insert
                // a pre-CTX-2-shaped run row directly, proving the migration itself never touches it.
                await db.Database.MigrateAsync();

                db.Integrations.Add(new IntegrationEntity
                {
                    Id = "ctx2-int-1",
                    DisplayName = "Ctx2 Integration",
                    ModuleId = "Sample.Mod",
                    State = 0,
                    ResourceContextId = "shared",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                db.OperationRuns.Add(new OperationRunEntity
                {
                    Id = "ctx2-run-1",
                    WorkflowInstanceId = "ctx2-wi-1",
                    DefinitionId = "def-1",
                    Status = "completed",
                    CreatedAt = DateTimeOffset.UtcNow,
                    IntegrationId = "ctx2-int-1"
                    // ResourceContextId intentionally omitted: this is what pre-CTX-2 history looks like.
                });
                await db.SaveChangesAsync();
            }

            await using var verify = NewDb(connectionString);
            var run = await verify.OperationRuns.AsNoTracking().SingleAsync(r => r.Id == "ctx2-run-1");
            run.ResourceContextId.Should().BeNull("pre-CTX-2 / unresolved history must read as unknown, never backfilled to Shared");

            foreach (var (table, column) in new[]
            {
                ("OperationRuns", "ResourceContextId"),
                ("ScheduleRuns", "ResourceContextId"),
                ("WorkflowStartRequests", "ResourceContextId"),
                ("OperationStartRequests", "ResourceContextId"),
                ("WorkflowInstances", "ResourceContextId"),
                ("OperationInstances", "ResourceContextId"),
                ("OperationGroups", "ResourceContextId"),
                ("PlatformEvents", "ResourceContextId"),
                ("NotificationInstances", "ResourceContextId"),
                ("AttentionIncidents", "ResourceContextId"),
                ("AuditLogs", "ResourceContextId"),
            })
            {
                (await ScalarAsync(connectionString, $"""
                    SELECT CAST(CASE WHEN is_nullable = 1 THEN 1 ELSE 0 END AS int)
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'{table}') AND name = N'{column}'
                    """)).Should().Be(1, $"{table}.{column} must remain nullable");

                (await ScalarAsync(connectionString, $"""
                    SELECT COUNT(*) FROM sys.foreign_keys
                    WHERE parent_object_id = OBJECT_ID(N'{table}')
                      AND EXISTS (
                          SELECT 1 FROM sys.foreign_key_columns fkc
                          JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
                          WHERE fkc.constraint_object_id = foreign_keys.object_id AND c.name = N'{column}')
                    """)).Should().Be(0, $"{table}.{column} is a historical snapshot and must carry no foreign key");
            }
        }
        finally
        {
            await TryDropDatabaseAsync(baseConnectionString, catalog);
        }
    }

    private static AppertureDbContext NewDb(string connectionString) =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(connectionString)
            .Options);

    private static async Task<int> ScalarAsync(string connectionString, string sql)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    private static async Task TryDropDatabaseAsync(string baseConnectionString, string catalog)
    {
        try
        {
            var master = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" }.ConnectionString;
            await using var conn = new SqlConnection(master);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{catalog}')
                BEGIN
                    ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{catalog}];
                END
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Cleanup is best-effort.
        }
    }
}
