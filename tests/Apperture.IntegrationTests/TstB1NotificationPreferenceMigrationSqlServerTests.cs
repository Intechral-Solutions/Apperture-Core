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
/// Toast-B P1 Task 1 migration evidence: <c>TstB1_NotificationTogglePreferences</c> adds a
/// NOT NULL <c>ToastEnabledByDefault</c> column (defaulting existing/other definitions to
/// <see langword="false"/> while flipping the four built-in definitions to <see langword="true"/>),
/// creates <c>NotificationUserPreferences</c> with its unique
/// (UserId, NotificationDefinitionId, Channel) index, adds the new
/// (PrincipalUserId, CreatedAtUtc, Id) cursor index on <c>NotificationUserInboxItems</c>, and
/// converts historical self-service "Email off" overrides into explicit
/// <c>email=false</c> + <c>toast=false</c> preference rows while leaving non-matching overrides
/// untouched. The qualifying override rows themselves are deliberately NOT deleted by this
/// migration -- <c>SqlNotificationRecipientResolver.IsOptedOut</c> still reads them directly, and
/// nothing yet consults the new preference rows, so deletion is deferred to phase P2.
/// </summary>
/// <remarks>
/// Each run builds its own throwaway catalog. Soft-skips when no reachable SQL endpoint is configured.
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class TstB1NotificationPreferenceMigrationSqlServerTests
{
    /// <summary>The last migration before TstB1.</summary>
    private const string PreTstB1Migration = "20260916173420_Ctx3B_AuditCorrelationId";

    private const string TstB1Migration = "20260922075738_TstB1_NotificationTogglePreferences";

    private const string ExpectedRunMissingDefinitionId = "platform.schedule.expected_run_missing";
    private const string HelloWorldDefinitionId = "module.sample.helloworld.demo.completed";
    private const string FileImportDefinitionId = "module.sample.fileimport.import.completed_with_errors";
    private const string ApiPollingDefinitionId = "module.sample.apipolling.sync.completed_with_errors";

    [Fact]
    public async Task TstB1_adds_toast_column_and_preferences_table_and_seeds_builtin_defaults()
    {
        var baseConnectionString = SqlServerTestConfiguration.GetConnectionString();
        if (baseConnectionString is null)
            return; // soft-skip: no reachable SQL endpoint configured

        var catalog = "Apperture_TstB1SchemaMigration_" + Guid.NewGuid().ToString("N")[..12];
        var connectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;

        try
        {
            SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);
        }
        catch (InvalidOperationException)
        {
            return; // soft-skip: endpoint unreachable
        }

        try
        {
            // ----------------------------------------------------------------------------------
            // Arrange: migrate to the last pre-TstB1 migration and add a non-built-in definition
            // row, so we can prove it defaults the new column to false rather than being swept up
            // by the built-in seed UPDATE.
            // ----------------------------------------------------------------------------------
            await using (var preDb = NewDb(connectionString))
            {
                if (!await preDb.Database.CanConnectAsync())
                    return; // soft-skip

                await preDb.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(PreTstB1Migration);

                (await ColumnExistsAsync(connectionString, "NotificationDefinitions", "ToastEnabledByDefault"))
                    .Should().BeFalse("the arrange step must genuinely predate TstB1");

                await ExecuteAsync(connectionString, """
                    INSERT INTO [NotificationDefinitions]
                        ([Id],[DisplayName],[SourceKind],[Origin],[Scope],[Mutability],[IsEnabled],
                         [RuleJson],[TemplateJson],[CreatedAtUtc],[UpdatedAtUtc])
                    VALUES
                        (N'tstb1-custom-def', N'Custom user definition', N'platformEvent', N'userCreated',
                         N'platform', N'fullyEditable', 1, N'{}', N'{}', SYSUTCDATETIME(), SYSUTCDATETIME());
                    """);
            }

            // ----------------------------------------------------------------------------------
            // Act: apply TstB1.
            // ----------------------------------------------------------------------------------
            await using (var migrateDb = NewDb(connectionString))
            {
                await migrateDb.Database.MigrateAsync();

                var applied = await migrateDb.Database.GetAppliedMigrationsAsync();
                applied.Should().Contain(TstB1Migration);
            }

            // ----------------------------------------------------------------------------------
            // Assert
            // ----------------------------------------------------------------------------------
            await using var db = NewDb(connectionString);

            (await ScalarAsync(connectionString, """
                SELECT CAST(CASE WHEN is_nullable = 0 THEN 1 ELSE 0 END AS int)
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'NotificationDefinitions') AND name = N'ToastEnabledByDefault'
                """)).Should().Be(1, "ToastEnabledByDefault must be NOT NULL");

            var custom = await db.NotificationDefinitions.AsNoTracking().SingleAsync(d => d.Id == "tstb1-custom-def");
            custom.ToastEnabledByDefault.Should().BeFalse("a pre-existing, non-built-in definition must default to Toast-off");

            foreach (var builtInId in new[]
                     {
                         ExpectedRunMissingDefinitionId, HelloWorldDefinitionId, FileImportDefinitionId, ApiPollingDefinitionId
                     })
            {
                var def = await db.NotificationDefinitions.AsNoTracking().SingleAsync(d => d.Id == builtInId);
                def.ToastEnabledByDefault.Should().BeTrue($"{builtInId} is one of the four built-in Toast defaults");
            }

            // NotificationUserPreferences exists with its unique index.
            (await ScalarAsync(connectionString, """
                SELECT COUNT(*) FROM sys.tables WHERE name = N'NotificationUserPreferences'
                """)).Should().Be(1);

            var prefIndexColumns = await OrderedIndexColumnsAsync(
                connectionString, "NotificationUserPreferences", "IX_NotificationUserPreferences_UserId_NotificationDefinitionId_Channel");
            prefIndexColumns.Should().Equal("UserId", "NotificationDefinitionId", "Channel");

            (await ScalarAsync(connectionString, """
                SELECT CAST(is_unique AS int) FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'NotificationUserPreferences')
                  AND name = N'IX_NotificationUserPreferences_UserId_NotificationDefinitionId_Channel'
                """)).Should().Be(1, "the (UserId, NotificationDefinitionId, Channel) index must be unique");

            // NotificationUserInboxItems cursor index.
            var inboxCursorColumns = await OrderedIndexColumnsAsync(
                connectionString, "NotificationUserInboxItems", "IX_NotificationUserInboxItems_PrincipalUserId_CreatedAtUtc_Id");
            inboxCursorColumns.Should().Equal("PrincipalUserId", "CreatedAtUtc", "Id");
        }
        finally
        {
            await TryDropDatabaseAsync(baseConnectionString, catalog);
        }
    }

    [Fact]
    public async Task TstB1_converts_matching_legacy_override_to_preferences_without_deleting_it_and_leaves_non_matching_overrides_untouched()
    {
        var baseConnectionString = SqlServerTestConfiguration.GetConnectionString();
        if (baseConnectionString is null)
            return; // soft-skip: no reachable SQL endpoint configured

        var catalog = "Apperture_TstB1LegacyMigration_" + Guid.NewGuid().ToString("N")[..12];
        var connectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;

        try
        {
            SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);
        }
        catch (InvalidOperationException)
        {
            return; // soft-skip: endpoint unreachable
        }

        const string matchingUserId = "tstb1-user-matching";
        const string teamOverrideUserId = "tstb1-user-team-override";
        const string adminSetUserId = "tstb1-user-admin-set";

        try
        {
            // ----------------------------------------------------------------------------------
            // Arrange: three NotificationSubscriptionUserOverrides rows against the pre-TstB1 schema.
            //   1. matchingUserId: OperationalTeamId IS NULL and CreatedByActorId = UserId -> converts.
            //   2. teamOverrideUserId: OperationalTeamId set -> not a self-service override, untouched.
            //   3. adminSetUserId: CreatedByActorId != UserId (an admin set it) -> untouched.
            // ----------------------------------------------------------------------------------
            await using (var preDb = NewDb(connectionString))
            {
                if (!await preDb.Database.CanConnectAsync())
                    return; // soft-skip

                await preDb.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(PreTstB1Migration);

                await ExecuteAsync(connectionString, $"""
                    INSERT INTO [NotificationSubscriptionUserOverrides]
                        ([Id],[UserId],[NotificationDefinitionId],[OperationalTeamId],[CreatedAtUtc],[CreatedByActorId])
                    VALUES
                        (N'noverride_tstb1_matching', N'{matchingUserId}', N'{ExpectedRunMissingDefinitionId}', NULL,
                         SYSUTCDATETIME(), N'{matchingUserId}'),
                        (N'noverride_tstb1_team', N'{teamOverrideUserId}', N'{ExpectedRunMissingDefinitionId}', N'tstb1-team-1',
                         SYSUTCDATETIME(), N'{teamOverrideUserId}'),
                        (N'noverride_tstb1_adminset', N'{adminSetUserId}', N'{ExpectedRunMissingDefinitionId}', NULL,
                         SYSUTCDATETIME(), N'tstb1-admin-actor');
                    """);
            }

            // ----------------------------------------------------------------------------------
            // Act: apply TstB1 only (not later migrations). This test proves TstB1's own,
            // isolated behavior -- the override row surviving TstB1 -- which is exactly what
            // TstB2_RetireLegacyEmailOptOutOverrides later deletes once it's safe to. Migrating to
            // latest here would run TstB2 too and delete the row this test asserts must still
            // exist, which is TstB2's job to prove (see
            // TstB2NotificationOverrideRetirementMigrationSqlServerTests), not this test's.
            // ----------------------------------------------------------------------------------
            await using (var migrateDb = NewDb(connectionString))
            {
                await migrateDb.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(TstB1Migration);
            }

            // ----------------------------------------------------------------------------------
            // Assert
            // ----------------------------------------------------------------------------------
            await using var db = NewDb(connectionString);

            // Matching row: converted into preference rows, but the override row itself is NOT
            // deleted -- SqlNotificationRecipientResolver.IsOptedOut still reads it directly for
            // team/attention-routed delivery paths, and nothing yet consults the new preference
            // channel, so deletion is deferred to phase P2's own migration.
            (await db.NotificationSubscriptionUserOverrides.AsNoTracking()
                .CountAsync(o => o.Id == "noverride_tstb1_matching")).Should().Be(
                    1, "the matching override must survive TstB1 -- deleting it now would silently resume " +
                       "email delivery, since nothing yet reads the new preference row it was converted to");

            var matchingPrefs = await db.NotificationUserPreferences.AsNoTracking()
                .Where(p => p.UserId == matchingUserId && p.NotificationDefinitionId == ExpectedRunMissingDefinitionId)
                .ToListAsync();
            matchingPrefs.Should().HaveCount(2, "both email and toast preference rows must be created");
            matchingPrefs.Should().OnlyContain(p => !p.IsEnabled, "converted preferences must be explicitly disabled");
            matchingPrefs.Select(p => p.Channel).Should().BeEquivalentTo(["email", "toast"]);
            matchingPrefs.Should().OnlyContain(
                p => p.Id.StartsWith("nupref_", StringComparison.Ordinal) && p.Id.Length == "nupref_".Length + 32,
                "new preference ids must follow the nupref_ + 32 lowercase hex digit convention");
            matchingPrefs.Should().OnlyContain(p => p.UpdatedByActorId == matchingUserId);

            // Team-scoped override: untouched.
            (await db.NotificationSubscriptionUserOverrides.AsNoTracking()
                .CountAsync(o => o.Id == "noverride_tstb1_team")).Should().Be(1, "a team-scoped override is not a self-service opt-out");
            (await db.NotificationUserPreferences.AsNoTracking()
                .CountAsync(p => p.UserId == teamOverrideUserId)).Should().Be(0);

            // Admin-set override (CreatedByActorId != UserId): untouched.
            (await db.NotificationSubscriptionUserOverrides.AsNoTracking()
                .CountAsync(o => o.Id == "noverride_tstb1_adminset")).Should().Be(1, "an admin-set override is not a self-service opt-out");
            (await db.NotificationUserPreferences.AsNoTracking()
                .CountAsync(p => p.UserId == adminSetUserId)).Should().Be(0);
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

    private static async Task<List<string>> OrderedIndexColumnsAsync(string connectionString, string table, string indexName)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.name
            FROM sys.index_columns ic
            JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@table) AND i.name = @indexName
            ORDER BY ic.key_ordinal
            """;
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@indexName", indexName);

        var columns = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));

        return columns;
    }

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
