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
/// Toast-B P2 Task 4 migration evidence: <c>TstB2_RetireLegacyEmailOptOutOverrides</c> deletes the
/// <c>NotificationSubscriptionUserOverrides</c> rows matching the exact same self-service predicate
/// <c>TstB1_NotificationTogglePreferences</c> used to convert them
/// (<c>OperationalTeamId IS NULL AND CreatedByActorId = UserId</c>). This is the deferred half of
/// that earlier conversion: it is only safe now that
/// <see cref="Apperture.Persistence.SqlServer.SqlNotificationRecipientResolver"/>'s recipient
/// eligibility check consults the per-user "email" preference channel and the self-service Email
/// switch writes only that preference (both Toast-B P2, landed before this migration). Admin-written
/// global opt-outs (<c>CreatedByActorId != UserId</c>) and all team-scoped opt-outs
/// (<c>OperationalTeamId</c> set) are structurally excluded by the predicate and must survive
/// untouched, and the deletion must be idempotent.
/// </summary>
/// <remarks>
/// Each run builds its own throwaway catalog. Soft-skips when no reachable SQL endpoint is configured.
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class TstB2NotificationOverrideRetirementMigrationSqlServerTests
{
    private const string TstB1Migration = "20260922075738_TstB1_NotificationTogglePreferences";
    private const string TstB2Migration = "20260922102920_TstB2_RetireLegacyEmailOptOutOverrides";

    private const string ExpectedRunMissingDefinitionId = "platform.schedule.expected_run_missing";

    private const string RetireDeletePredicateSql = """
        DELETE FROM [NotificationSubscriptionUserOverrides]
        WHERE [OperationalTeamId] IS NULL AND [CreatedByActorId] = [UserId];
        """;

    [Fact]
    public async Task TstB2_deletes_matching_self_service_overrides_leaves_admin_and_team_scoped_untouched_and_is_idempotent()
    {
        var baseConnectionString = SqlServerTestConfiguration.GetConnectionString();
        if (baseConnectionString is null)
            return; // soft-skip: no reachable SQL endpoint configured

        var catalog = "Apperture_TstB2RetireMigration_" + Guid.NewGuid().ToString("N")[..12];
        var connectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;

        try
        {
            SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);
        }
        catch (InvalidOperationException)
        {
            return; // soft-skip: endpoint unreachable
        }

        const string selfServiceUserId = "tstb2-user-self-service";
        const string teamOverrideUserId = "tstb2-user-team-override";
        const string adminSetUserId = "tstb2-user-admin-set";

        try
        {
            // ----------------------------------------------------------------------------------
            // Arrange: migrate up to (and including) TstB1 -- the resolver's read side and the
            // self-service switch's write side are Toast-B P2 code, not schema, so nothing further
            // is required here to make the scenario realistic -- then insert three override rows
            // directly against the still-live NotificationSubscriptionUserOverrides table:
            //   1. selfServiceUserId: OperationalTeamId IS NULL and CreatedByActorId = UserId
            //      -> matches the retirement predicate, must be deleted by TstB2.
            //   2. teamOverrideUserId: OperationalTeamId set -> a team-scoped opt-out, a separate,
            //      still-fully-functional mechanism untouched by this whole phase.
            //   3. adminSetUserId: CreatedByActorId != UserId (an admin set it) -> a global opt-out
            //      the retiring user did not self-serve.
            // ----------------------------------------------------------------------------------
            await using (var preDb = NewDb(connectionString))
            {
                if (!await preDb.Database.CanConnectAsync())
                    return; // soft-skip

                await preDb.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(TstB1Migration);

                await ExecuteAsync(connectionString, $"""
                    INSERT INTO [NotificationSubscriptionUserOverrides]
                        ([Id],[UserId],[NotificationDefinitionId],[OperationalTeamId],[CreatedAtUtc],[CreatedByActorId])
                    VALUES
                        (N'noverride_tstb2_selfservice', N'{selfServiceUserId}', N'{ExpectedRunMissingDefinitionId}', NULL,
                         SYSUTCDATETIME(), N'{selfServiceUserId}'),
                        (N'noverride_tstb2_team', N'{teamOverrideUserId}', N'{ExpectedRunMissingDefinitionId}', N'tstb2-team-1',
                         SYSUTCDATETIME(), N'{teamOverrideUserId}'),
                        (N'noverride_tstb2_adminset', N'{adminSetUserId}', N'{ExpectedRunMissingDefinitionId}', NULL,
                         SYSUTCDATETIME(), N'tstb2-admin-actor');
                    """);
            }

            // ----------------------------------------------------------------------------------
            // Act: apply TstB2.
            // ----------------------------------------------------------------------------------
            await using (var migrateDb = NewDb(connectionString))
            {
                await migrateDb.Database.MigrateAsync();

                var applied = await migrateDb.Database.GetAppliedMigrationsAsync();
                applied.Should().Contain(TstB2Migration);
            }

            // ----------------------------------------------------------------------------------
            // Assert: positive case + both negative cases.
            // ----------------------------------------------------------------------------------
            await using (var db = NewDb(connectionString))
            {
                (await db.NotificationSubscriptionUserOverrides.AsNoTracking()
                    .CountAsync(o => o.Id == "noverride_tstb2_selfservice")).Should().Be(
                        0, "the self-service override must be retired now that the email preference " +
                           "channel and the self-service switch both consult/write it instead");

                (await db.NotificationSubscriptionUserOverrides.AsNoTracking()
                    .CountAsync(o => o.Id == "noverride_tstb2_team")).Should().Be(
                        1, "a team-scoped override is a separate mechanism and must survive untouched");

                (await db.NotificationSubscriptionUserOverrides.AsNoTracking()
                    .CountAsync(o => o.Id == "noverride_tstb2_adminset")).Should().Be(
                        1, "an admin-set global override is not a self-service opt-out and must survive untouched");
            }

            // ----------------------------------------------------------------------------------
            // Idempotency: re-running the exact same predicate deletion a second time must be a
            // no-op (zero rows affected, no error), matching TstB2's own Up() comment.
            // ----------------------------------------------------------------------------------
            var secondRunRowsAffected = await ExecuteNonQueryAsync(connectionString, RetireDeletePredicateSql);
            secondRunRowsAffected.Should().Be(0, "a second run of the retirement predicate must delete nothing");

            await using (var db = NewDb(connectionString))
            {
                (await db.NotificationSubscriptionUserOverrides.AsNoTracking()
                    .CountAsync(o => o.Id == "noverride_tstb2_team")).Should().Be(1, "unaffected by the idempotent re-run");
                (await db.NotificationSubscriptionUserOverrides.AsNoTracking()
                    .CountAsync(o => o.Id == "noverride_tstb2_adminset")).Should().Be(1, "unaffected by the idempotent re-run");
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

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> ExecuteNonQueryAsync(string connectionString, string sql)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteNonQueryAsync();
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
