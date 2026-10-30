using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// CTX-1 migration evidence against a populated pre-CTX-1 database: the reserved Shared context is
/// seeded, every existing Integration, Connection and ScheduleDefinition backfills to it, no resource
/// id is rewritten, and the ownership columns end up required with indexes and foreign keys in place.
/// </summary>
/// <remarks>
/// Each run builds its own throwaway catalog so the populated pre-migration state is genuine rather
/// than whatever a shared test database happens to hold, and so the drop at the end cannot disturb the
/// shared integration database. Without a reachable SQL endpoint it returns early in optional mode and fails when
/// APPERTURE_REQUIRE_SQL_TESTS=1 (see <see cref="SqlTestExecution"/>).
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ResourceContextMigrationSqlServerTests
{
    /// <summary>The last migration before ResourceContexts existed.</summary>
    private const string PreCtx1Migration = "20260910170000_NtfC_DropLegacyAttentionNotificationTables";

    private const string Ctx1Migration = "20260911120000_Ctx1_ResourceContextFoundation";

    [Fact]
    public async Task Ctx1_seeds_shared_and_backfills_every_existing_context_owned_resource()
    {
        var baseConnectionString = SqlTestExecution.ConnectionStringOrNotExecuted();
        if (baseConnectionString is null)
            return;

        var catalog = "Apperture_Ctx1Migration_" + Guid.NewGuid().ToString("N")[..12];
        var connectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;

        try
        {
            SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);
        }
        catch (InvalidOperationException)
        {
            SqlTestExecution.NotExecuted("the test SQL Server could not be reached to create the catalog");
            return;
        }

        try
        {
            // ----------------------------------------------------------------------------------
            // Arrange: a populated database on the schema as it stood before CTX-1.
            // ----------------------------------------------------------------------------------
            await using (var preDb = NewDb(connectionString))
            {
                if (!await preDb.Database.CanConnectAsync())
                {
                    SqlTestExecution.NotExecuted("the test catalog was created but cannot be connected to");
                    return;
                }

                await preDb.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(PreCtx1Migration);

                (await ColumnExistsAsync(connectionString, "Integrations", "ResourceContextId"))
                    .Should().BeFalse("the arrange step must genuinely predate CTX-1");

                await ExecuteAsync(connectionString, """
                    INSERT INTO [Integrations] ([Id], [DisplayName], [ModuleId], [ScopeKey], [State], [SettingsJson], [CreatedAt], [UpdatedAt])
                    VALUES
                        (N'ctx1-int-1', N'Existing Integration One', N'Sample.Mod', NULL, 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
                        (N'ctx1-int-2', N'Existing Integration Two', N'Sample.Mod', N'environment:prod', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

                    INSERT INTO [Connections] ([Id], [ConnectorDefinitionId], [Name], [ConfigJson], [SecretRefsJson], [ScopeKey], [OwnerId], [State], [LastVerificationStatus])
                    VALUES
                        (N'ctx1-conn-1', N'platform.sql', N'Existing Connection One', N'{}', N'{}', NULL, NULL, 0, 0),
                        (N'ctx1-conn-2', N'platform.sql', N'Existing Connection Two', N'{}', N'{}', NULL, NULL, 0, 0);

                    INSERT INTO [ScheduleDefinitions] ([Id], [IntegrationId], [Name], [RecurrenceExpression], [TimeZone], [Enabled], [TargetJobType], [TargetJobConfigurationJson], [ExecutionPolicyRef], [Version])
                    VALUES
                        (N'ctx1-sched-1', N'ctx1-int-1', N'Attached Schedule', N'0 0 * * *', N'UTC', 1, 0, N'{}', NULL, 1),
                        (N'ctx1-sched-2', NULL, N'Standalone Schedule', N'0 1 * * *', N'UTC', 1, 0, N'{}', NULL, 1);
                    """);
            }

            // ----------------------------------------------------------------------------------
            // Act: apply CTX-1. No operator classification is required.
            // ----------------------------------------------------------------------------------
            await using (var migrateDb = NewDb(connectionString))
            {
                await migrateDb.Database.MigrateAsync();

                var applied = await migrateDb.Database.GetAppliedMigrationsAsync();
                applied.Should().Contain(Ctx1Migration);
            }

            // ----------------------------------------------------------------------------------
            // Assert
            // ----------------------------------------------------------------------------------
            await using var db = NewDb(connectionString);

            // The reserved Shared context exists exactly once, seeded by the migration itself.
            var shared = await db.ResourceContexts.AsNoTracking()
                .SingleAsync(c => c.Id == ResourceContextIds.SharedValue);
            shared.Name.Should().Be("Shared");
            shared.Slug.Should().Be("shared");
            shared.Kind.Should().Be(ResourceContextKinds.Shared);
            shared.State.Should().Be(ResourceContextStates.Active);
            shared.IsReserved.Should().BeTrue();

            (await db.ResourceContexts.AsNoTracking().CountAsync(c => c.IsReserved)).Should().Be(1);

            // Every pre-existing row is now owned by Shared, and no id was rewritten.
            var integrations = await db.Integrations.AsNoTracking().Where(e => e.Id.StartsWith("ctx1-int-")).ToListAsync();
            integrations.Select(e => e.Id).Should().BeEquivalentTo(["ctx1-int-1", "ctx1-int-2"]);
            integrations.Should().OnlyContain(e => e.ResourceContextId == ResourceContextIds.SharedValue);
            integrations.Single(e => e.Id == "ctx1-int-2").ScopeKey
                .Should().Be("environment:prod", "ScopeKey is authorization and must be untouched by ownership");

            var connections = await db.Connections.AsNoTracking().Where(e => e.Id.StartsWith("ctx1-conn-")).ToListAsync();
            connections.Select(e => e.Id).Should().BeEquivalentTo(["ctx1-conn-1", "ctx1-conn-2"]);
            connections.Should().OnlyContain(e => e.ResourceContextId == ResourceContextIds.SharedValue);

            var schedules = await db.ScheduleDefinitions.AsNoTracking().Where(e => e.Id.StartsWith("ctx1-sched-")).ToListAsync();
            schedules.Select(e => e.Id).Should().BeEquivalentTo(["ctx1-sched-1", "ctx1-sched-2"]);
            schedules.Should().OnlyContain(e => e.ResourceContextId == ResourceContextIds.SharedValue);

            // No row anywhere escaped the backfill.
            (await db.Integrations.AsNoTracking().CountAsync(e => e.ResourceContextId != ResourceContextIds.SharedValue)).Should().Be(0);
            (await db.Connections.AsNoTracking().CountAsync(e => e.ResourceContextId != ResourceContextIds.SharedValue)).Should().Be(0);
            (await db.ScheduleDefinitions.AsNoTracking().CountAsync(e => e.ResourceContextId != ResourceContextIds.SharedValue)).Should().Be(0);

            // Ownership is required.
            foreach (var table in new[] { "Integrations", "Connections", "ScheduleDefinitions" })
            {
                (await ScalarAsync(connectionString, $"""
                    SELECT CAST(CASE WHEN is_nullable = 0 THEN 1 ELSE 0 END AS int)
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'{table}') AND name = N'ResourceContextId'
                    """)).Should().Be(1, $"{table}.ResourceContextId must be NOT NULL");

                (await ScalarAsync(connectionString, $"""
                    SELECT COUNT(*) FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'{table}') AND name = N'IX_{table}_ResourceContextId'
                    """)).Should().Be(1, $"{table} must have a non-unique index on ResourceContextId");

                (await ScalarAsync(connectionString, $"""
                    SELECT COUNT(*) FROM sys.foreign_keys
                    WHERE parent_object_id = OBJECT_ID(N'{table}')
                      AND referenced_object_id = OBJECT_ID(N'ResourceContexts')
                      AND delete_referential_action = 0
                    """)).Should().Be(1, $"{table} must have a NO ACTION foreign key to ResourceContexts");
            }

            // Slug uniqueness is enforced by the database, not only by the service.
            (await ScalarAsync(connectionString, """
                SELECT COUNT(*) FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'ResourceContexts') AND name = N'UX_ResourceContexts_Slug' AND is_unique = 1
                """)).Should().Be(1);

            // The foreign key is structural backup for the archive-only rule: a context that owns
            // resources cannot be deleted even by raw SQL.
            var deleteShared = async () => await ExecuteAsync(connectionString, "DELETE FROM [ResourceContexts] WHERE [Id] = N'shared';");
            await deleteShared.Should().ThrowAsync<SqlException>();
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

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(string connectionString, string sql)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    private static async Task<bool> ColumnExistsAsync(string connectionString, string table, string column) =>
        await ScalarAsync(connectionString, $"""
            SELECT COUNT(*) FROM sys.columns
            WHERE object_id = OBJECT_ID(N'{table}') AND name = N'{column}'
            """) > 0;

    private static async Task TryDropDatabaseAsync(string baseConnectionString, string catalog)
    {
        try
        {
            var master = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" }.ConnectionString;
            await ExecuteAsync(master, $"""
                IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{catalog}')
                BEGIN
                    ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{catalog}];
                END
                """);
        }
        catch
        {
            // Cleanup is best-effort; a leftover throwaway catalog must not fail the run.
        }
    }
}
