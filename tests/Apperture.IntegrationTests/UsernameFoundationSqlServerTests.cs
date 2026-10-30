using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.IntegrationTests;

/// <summary>
/// Real SQL Server proof for the username foundation: declared collation, case-insensitive uniqueness,
/// the permanent ledger, one Local link per user, the populated-database migration through the
/// backfill barrier, concurrency, and transactional rename and reclaim.
/// </summary>
/// <remarks>
/// Each test uses its own throwaway catalog. Without a reachable SQL endpoint the tests return early
/// (and write "NOT EXECUTED" to the test output). Set <c>APPERTURE_REQUIRE_SQL_TESTS=1</c> to turn that
/// into a failure, so an absent server can never be mistaken for proof.
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class UsernameFoundationSqlServerTests
{
    private const string PreUsernameMigration = "20260922102920_TstB2_RetireLegacyEmailOptOutOverrides";
    private const string UsernameLedgerMigration = "20261004120000_Iam9_UsernameLedger";
    private const string UsernameRequiredMigration = "20261004120100_Iam9_UsernameRequired";

    private readonly ITestOutputHelper _output;

    public UsernameFoundationSqlServerTests(ITestOutputHelper output) => _output = output;

    // -------------------------------------------------------------------------------------------
    // Schema: collation, case-insensitive uniqueness, ledger key, indexes
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Username_columns_declare_the_explicit_case_insensitive_collation()
    {
        await using var scratch = await CreateMigratedAsync("Apperture_Iam9Collation_");
        if (scratch is null)
            return;

        var collations = await QueryAsync(scratch.ConnectionString, """
            SELECT OBJECT_NAME(c.object_id) + N'.' + c.name, c.collation_name
            FROM sys.columns c
            WHERE (c.object_id = OBJECT_ID(N'dbo.Users') AND c.name = N'Username')
               OR (c.object_id = OBJECT_ID(N'dbo.UsernameLedger') AND c.name = N'Username');
            """);

        collations.Should().BeEquivalentTo(new[]
        {
            ("Users.Username", "Latin1_General_100_CI_AS"),
            ("UsernameLedger.Username", "Latin1_General_100_CI_AS"),
        });

        var nullable = await ScalarAsync<bool>(scratch.ConnectionString,
            "SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'Username';");
        nullable.Should().BeFalse("every user has a username once the foundation is applied");
        _output.WriteLine("EXECUTED against SQL Server catalog " + scratch.Catalog);
    }

    [Fact]
    public async Task Users_username_is_unique_case_insensitively_at_the_database()
    {
        await using var scratch = await CreateMigratedAsync("Apperture_Iam9UsersCi_");
        if (scratch is null)
            return;

        await ExecuteAsync(scratch.ConnectionString, InsertUserSql("u1", "alice"));

        var act = async () => await ExecuteAsync(scratch.ConnectionString, InsertUserSql("u2", "ALICE"));

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().BeOneOf(2601, 2627);
        (await ScalarAsync<int>(scratch.ConnectionString, "SELECT COUNT(*) FROM Users;")).Should().Be(1);
        _output.WriteLine("EXECUTED against SQL Server catalog " + scratch.Catalog);
    }

    [Fact]
    public async Task Ledger_username_is_the_case_insensitive_primary_key_even_through_raw_sql()
    {
        await using var scratch = await CreateMigratedAsync("Apperture_Iam9LedgerCi_");
        if (scratch is null)
            return;

        await ExecuteAsync(scratch.ConnectionString, InsertUserSql("u1", "alice") + InsertUserSql("u2", "bob"));
        await ExecuteAsync(scratch.ConnectionString, InsertLedgerSql("alice", "u1", retired: false));

        // Another user can never take the name, current or retired, in any case.
        var differentCase = async () => await ExecuteAsync(scratch.ConnectionString, InsertLedgerSql("ALICE", "u2", retired: true));
        (await differentCase.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(2627);

        await ExecuteAsync(scratch.ConnectionString,
            "UPDATE UsernameLedger SET RetiredAtUtc = SYSDATETIMEOFFSET(), RetiredReason = N'rename' WHERE Username = N'alice';");
        var retiredTaken = async () => await ExecuteAsync(scratch.ConnectionString, InsertLedgerSql("Alice", "u2", retired: false));
        (await retiredTaken.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(2627);

        (await ScalarAsync<string>(scratch.ConnectionString, "SELECT UserId FROM UsernameLedger WHERE Username = N'alice';"))
            .Should().Be("u1");
        _output.WriteLine("EXECUTED against SQL Server catalog " + scratch.Catalog);
    }

    [Fact]
    public async Task Ledger_allows_one_current_name_per_user_and_a_bounded_retired_reason()
    {
        await using var scratch = await CreateMigratedAsync("Apperture_Iam9LedgerCurrent_");
        if (scratch is null)
            return;

        await ExecuteAsync(scratch.ConnectionString, InsertUserSql("u1", "alice"));
        await ExecuteAsync(scratch.ConnectionString, InsertLedgerSql("alice", "u1", retired: false));

        var secondCurrent = async () => await ExecuteAsync(scratch.ConnectionString, InsertLedgerSql("alice2", "u1", retired: false));
        (await secondCurrent.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(2601);

        var badReason = async () => await ExecuteAsync(scratch.ConnectionString, """
            INSERT INTO UsernameLedger (Username, UserId, AssignedAtUtc, RetiredAtUtc, RetiredReason)
            VALUES (N'alice3', N'u1', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'other');
            """);
        (await badReason.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(547);
        _output.WriteLine("EXECUTED against SQL Server catalog " + scratch.Catalog);
    }

    [Fact]
    public async Task One_local_link_per_user_is_enforced_and_entra_uniqueness_is_unchanged()
    {
        await using var scratch = await CreateMigratedAsync("Apperture_Iam9Links_");
        if (scratch is null)
            return;

        var indexes = await QueryAsync(scratch.ConnectionString, """
            SELECT i.name, CONCAT(CAST(i.is_unique AS nvarchar(1)), N'|', ISNULL(i.filter_definition, N''))
            FROM sys.indexes i
            WHERE i.object_id = OBJECT_ID(N'dbo.IdentityLinks') AND i.name IS NOT NULL AND i.is_primary_key = 0;
            """);
        indexes.Should().Contain(("UX_IdentityLinks_Entra_Source_TenantId_ExternalId", "1|([Source]=(0) AND [TenantId] IS NOT NULL)"));
        indexes.Should().Contain(("UX_IdentityLinks_Local_Source_ExternalId", "1|([Source]=(1))"));
        indexes.Should().Contain(("UX_IdentityLinks_Local_UserId", "1|([Source]=(1))"));
        indexes.Should().Contain(("IX_IdentityLinks_UserId", "0|"));
        (await ScalarAsync<int>(scratch.ConnectionString,
            "SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'CK_IdentityLinks_EntraRequiresTenant';")).Should().Be(1);

        await ExecuteAsync(scratch.ConnectionString, InsertUserSql("u1", "alice") + InsertUserSql("u2", "bob"));
        await ExecuteAsync(scratch.ConnectionString, InsertLinkSql("l1", "u1", 1, "alice", null));

        var secondLocal = async () => await ExecuteAsync(scratch.ConnectionString, InsertLinkSql("l2", "u1", 1, "alice-other", null));
        (await secondLocal.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(2601);

        // Entra keys stay tenant + object id: the same oid may exist in another tenant, never twice in one.
        await ExecuteAsync(scratch.ConnectionString, InsertLinkSql("e1", "u1", 0, "oid-1", "tenant-a"));
        await ExecuteAsync(scratch.ConnectionString, InsertLinkSql("e2", "u2", 0, "oid-1", "tenant-b"));
        var duplicateEntra = async () => await ExecuteAsync(scratch.ConnectionString, InsertLinkSql("e3", "u2", 0, "oid-1", "tenant-a"));
        (await duplicateEntra.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(2601);
        _output.WriteLine("EXECUTED against SQL Server catalog " + scratch.Catalog);
    }

    // -------------------------------------------------------------------------------------------
    // Populated-database migration through the barrier
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Populated_database_migrates_through_the_backfill_barrier_and_reruns_safely()
    {
        await using var scratch = await CreateEmptyAsync("Apperture_Iam9Backfill_");
        if (scratch is null)
            return;

        await using (var db = scratch.NewDb())
            await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(PreUsernameMigration);

        await ExecuteAsync(scratch.ConnectionString, PopulatedSeedSql);

        // A tool that applies migrations directly skips the data step and must fail closed, not half-apply.
        await using (var db = scratch.NewDb())
        {
            var direct = async () => await db.Database.MigrateAsync();
            (await direct.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(50012);
            (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(UsernameLedgerMigration)
                .And.NotContain(UsernameRequiredMigration);
        }

        // The data step reports what it did; a second run finds nothing left to do.
        UsernameBackfillReport first;
        UsernameBackfillReport second;
        await using (var db = scratch.NewDb())
        {
            first = await UsernameBackfill.RunAsync(db, NullLogger.Instance, randomSuffix: () => "rnd000");
            second = await UsernameBackfill.RunAsync(db, NullLogger.Instance, randomSuffix: () => "rnd000");
        }

        _output.WriteLine(
            $"Backfill counts: LocalPreserved={first.LocalPreserved} LocalGrandfathered={first.LocalGrandfathered} Generated={first.Generated} " +
            $"GeneratedWithSuffix={first.GeneratedWithSuffix} ReservedNameHolders={first.ReservedNameHolders} " +
            $"PreviousRecorded={first.PreviousRecoveryAccessNamesRecorded} PreviousSkipped={first.PreviousRecoveryAccessNamesSkipped}");

        first.LocalPreserved.Should().Be(5, "alice, jane@contoso.com, dave (mixed), breakglass and system");
        first.LocalGrandfathered.Should().Be(1, "jane@contoso.com");
        first.ReservedNameHolders.Should().Be(1, "the person who already holds 'system'");
        first.Generated.Should().Be(6);
        first.GeneratedWithSuffix.Should().Be(2, "alice-2 and user-2");
        first.PreviousRecoveryAccessNamesRecorded.Should().Be(1, "pat.admin");
        first.PreviousRecoveryAccessNamesSkipped.Should().Be(
            4,
            "'alice' is held by another user, 'shared.name' is ambiguous, 'Mixed.Case' is not canonical and one row cannot be read");

        second.LocalPreserved.Should().Be(0);
        second.Generated.Should().Be(0);
        second.PreviousRecoveryAccessNamesRecorded.Should().Be(0);
        second.PreviousRecoveryAccessNamesSkipped.Should().Be(4, "the same historical rows are still reported, and still not acted on");

        // Simulate a start interrupted between the ledger and the Users write (not possible inside one
        // transaction, but the step must still converge): the user's current ledger row is kept.
        await ExecuteAsync(scratch.ConnectionString, "UPDATE Users SET Username = NULL WHERE Id = N'u_entra_upn';");
        await using (var db = scratch.NewDb())
        {
            var repair = await UsernameBackfill.RunAsync(db, NullLogger.Instance);
            repair.Generated.Should().Be(0, "the user already owns a current ledger name");
        }

        // The supported entry point completes the sequence.
        await using (var db = scratch.NewDb())
        {
            await AppertureDatabaseMigrator.MigrateAsync(db);
            (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }

        var usernames = (await QueryAsync(scratch.ConnectionString, "SELECT Id, Username FROM Users;")).ToDictionary(r => r.Item1, r => r.Item2);
        usernames.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["u_local"] = "alice",
            ["u_email"] = "jane@contoso.com",
            ["u_entra_upn"] = "bob.jones",
            ["u_entra_req"] = "carol",
            ["u_entra_none"] = "microsoft.user",
            ["u_boot1"] = "user",
            ["u_boot2"] = "user-2",
            ["u_entra_collide"] = "alice-2",
            ["u_mixed"] = "dave",
            ["u_ra"] = "breakglass",
            ["u_system"] = "system",
        });

        // Local links mirror usernames; the grandfathered name was lower-cased, not rewritten.
        (await QueryAsync(scratch.ConnectionString, """
            SELECT il.UserId, il.ExternalId FROM IdentityLinks il WHERE il.Source = 1;
            """)).Should().OnlyContain(r => usernames[r.Item1] == r.Item2);
        (await ScalarAsync<string>(scratch.ConnectionString, "SELECT ExternalId FROM IdentityLinks WHERE Id = N'l_email';"))
            .Should().Be("jane@contoso.com");

        // Every user has exactly one current ledger row equal to Users.Username.
        (await ScalarAsync<int>(scratch.ConnectionString, """
            SELECT COUNT(*) FROM Users u
            WHERE (SELECT COUNT(*) FROM UsernameLedger l WHERE l.UserId = u.Id AND l.RetiredAtUtc IS NULL AND l.Username = u.Username) <> 1;
            """)).Should().Be(0);

        // Historical Recovery Access names: recorded when safe, never seized from the current holder.
        (await QueryAsync(scratch.ConnectionString, "SELECT Username, CONCAT(UserId, N'|', RetiredReason) FROM UsernameLedger WHERE RetiredAtUtc IS NOT NULL;"))
            .Should().BeEquivalentTo(new[] { ("pat.admin", "u_ra|backfill_previous") });
        (await ScalarAsync<string>(scratch.ConnectionString, "SELECT UserId FROM UsernameLedger WHERE Username = N'alice';")).Should().Be("u_local");
        (await ScalarAsync<int>(scratch.ConnectionString, "SELECT COUNT(*) FROM UsernameLedger WHERE Username = N'shared.name';")).Should().Be(0);
        (await ScalarAsync<int>(scratch.ConnectionString, "SELECT COUNT(*) FROM UsernameLedger WHERE Username = N'mixed.case';"))
            .Should().Be(0, "a name that cannot be read exactly is never guessed at or claimed");

        // One audit row per generated name, by the system actor.
        (await ScalarAsync<int>(scratch.ConnectionString,
            "SELECT COUNT(*) FROM AuditLogs WHERE Action = N'identity.username.assigned' AND Actor = N'system';")).Should().Be(6);

        // Sign-in continuity: local sign-in still resolves through the Local link with the same input.
        await using (var db = scratch.NewDb())
        {
            var repository = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
            (await repository.GetByIdentityLinkAsync(IdentitySource.Local, "jane@contoso.com"))!.Id.Value.Should().Be("u_email");
            (await repository.GetByIdentityLinkAsync(IdentitySource.Local, "alice"))!.Username.Should().Be("alice");
            (await repository.GetByIdentityLinkAsync(ProviderIdentityKey.Entra("tenant-1", "oid-upn")))!.Username.Should().Be("bob.jones");
        }

        // Running the supported entry point again is a no-op.
        await using (var db = scratch.NewDb())
            await AppertureDatabaseMigrator.MigrateAsync(db);
        (await ScalarAsync<int>(scratch.ConnectionString,
            "SELECT COUNT(*) FROM AuditLogs WHERE Action = N'identity.username.assigned';")).Should().Be(6);
        _output.WriteLine("EXECUTED against SQL Server catalog " + scratch.Catalog);
    }

    [Fact]
    public async Task Duplicate_local_links_fail_the_migration_closed_without_choosing_one()
    {
        await using var scratch = await CreateEmptyAsync("Apperture_Iam9DupLocal_");
        if (scratch is null)
            return;

        await using (var db = scratch.NewDb())
            await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(PreUsernameMigration);

        await ExecuteAsync(scratch.ConnectionString,
            LegacyUserSql("u1", "Pat", false) + LegacyLinkSql("l1", "u1", 1, "pat", null, null, null) + LegacyLinkSql("l2", "u1", 1, "pat2", null, null, null));

        await using (var db = scratch.NewDb())
        {
            var act = async () => await AppertureDatabaseMigrator.MigrateAsync(db);
            var error = (await act.Should().ThrowAsync<SqlException>()).Which;
            error.Number.Should().Be(50011);
            error.Message.Should().Contain("more than one Local identity link");
            (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(UsernameLedgerMigration);
        }

        (await ScalarAsync<int>(scratch.ConnectionString, "SELECT COUNT(*) FROM IdentityLinks WHERE UserId = N'u1';")).Should().Be(2);
        _output.WriteLine("EXECUTED against SQL Server catalog " + scratch.Catalog);
    }

    // -------------------------------------------------------------------------------------------
    // Concurrency, rename, reclaim
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_generation_of_the_same_base_yields_distinct_correctly_owned_names()
    {
        await using var scratch = await CreateMigratedAsync("Apperture_Iam9Concurrent_");
        if (scratch is null)
            return;

        const int people = 6;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(1, people).Select(async i =>
        {
            await gate.Task;
            await using var db = scratch.NewDb();
            var repository = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
            var userId = new UserId("concurrent-" + i);
            return await new UsernameService(repository).AddWithGeneratedUsernameAsync(
                name => new User(userId, "Sam Lee", [new IdentityLink(IdentitySource.Entra, "oid-" + i, null, TenantId: "tenant-1")], [], false)
                {
                    Username = name
                },
                new UsernameGenerationSources("sam@contoso.com", null, null));
        }).ToList();
        gate.SetResult();
        var created = await Task.WhenAll(tasks);

        created.Select(u => u.Username).Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo(new[] { "sam" }.Concat(Enumerable.Range(2, people - 1).Select(n => $"sam-{n}")));
        foreach (var user in created)
        {
            (await ScalarAsync<string>(scratch.ConnectionString, $"SELECT UserId FROM UsernameLedger WHERE Username = N'{user.Username}';"))
                .Should().Be(user.Id.Value);
            (await ScalarAsync<string>(scratch.ConnectionString, $"SELECT Username FROM Users WHERE Id = N'{user.Id.Value}';"))
                .Should().Be(user.Username);
        }
        _output.WriteLine("EXECUTED against SQL Server catalog " + scratch.Catalog);
    }

    [Fact]
    public async Task Rename_moves_username_ledger_and_local_link_together_or_not_at_all()
    {
        await using var scratch = await CreateMigratedAsync("Apperture_Iam9Rename_");
        if (scratch is null)
            return;

        await AddLocalUserAsync(scratch, "u1", "alice");

        // Fault injected after the ledger rows are rewritten and before Users and the Local link save.
        await using (var failing = scratch.NewDb(new FailingSaveInterceptor()))
        {
            var repository = new SqlUserRepository(failing, NullLogger<SqlUserRepository>.Instance);
            var act = async () => await new UsernameService(repository).RenameAsync(new UserId("u1"), "alice.new");
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*injected*");
        }

        await AssertUserStateAsync(scratch, "u1", username: "alice", localLink: "alice", currentLedger: "alice");
        (await ScalarAsync<int>(scratch.ConnectionString, "SELECT COUNT(*) FROM UsernameLedger;")).Should().Be(1, "nothing was half-written");

        await using (var db = scratch.NewDb())
        {
            var repository = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
            var renamed = await new UsernameService(repository).RenameWithResultAsync(new UserId("u1"), "Alice.New");
            renamed.Outcome.Should().Be(UsernameChangeOutcome.Assigned);
            renamed.PreviousUsername.Should().Be("alice", "the transaction reports the name it replaced");
            renamed.Username.Should().Be("alice.new", "and the canonical form it stored");
        }

        await AssertUserStateAsync(scratch, "u1", username: "alice.new", localLink: "alice.new", currentLedger: "alice.new");
        (await ScalarAsync<string>(scratch.ConnectionString, "SELECT CONCAT(UserId, N'|', RetiredReason) FROM UsernameLedger WHERE Username = N'alice';"))
            .Should().Be("u1|rename");

        // The sign-in key moved with the username.
        await using (var db = scratch.NewDb())
        {
            var repository = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
            (await repository.GetByIdentityLinkAsync(IdentitySource.Local, "alice.new"))!.Id.Value.Should().Be("u1");
            (await repository.GetByIdentityLinkAsync(IdentitySource.Local, "alice")).Should().BeNull();
        }
        _output.WriteLine("EXECUTED against SQL Server catalog " + scratch.Catalog);
    }

    [Fact]
    public async Task Reclaim_is_atomic_and_never_takes_another_users_historical_name()
    {
        await using var scratch = await CreateMigratedAsync("Apperture_Iam9Reclaim_");
        if (scratch is null)
            return;

        await AddLocalUserAsync(scratch, "u1", "alice");
        await AddLocalUserAsync(scratch, "u2", "bob");
        await using (var db = scratch.NewDb())
        {
            var service = new UsernameService(new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance));
            await service.RenameAsync(new UserId("u1"), "alice.smith");
        }

        // Another user cannot take u1's retired or current names, through the service or raw SQL.
        await using (var db = scratch.NewDb())
        {
            var service = new UsernameService(new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance));
            var retired = async () => await service.RenameAsync(new UserId("u2"), "alice");
            (await retired.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(UsernameErrorCodes.Taken);
        }
        await using (var db = scratch.NewDb())
        {
            var service = new UsernameService(new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance));
            var current = async () => await service.RenameAsync(new UserId("u2"), "ALICE.SMITH");
            (await current.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(UsernameErrorCodes.Taken);
        }
        var raw = async () => await ExecuteAsync(scratch.ConnectionString, InsertLedgerSql("alice", "u2", retired: false));
        (await raw.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(2627);
        await AssertUserStateAsync(scratch, "u2", username: "bob", localLink: "bob", currentLedger: "bob");

        // A failed reclaim leaves everything as it was.
        await using (var failing = scratch.NewDb(new FailingSaveInterceptor()))
        {
            var service = new UsernameService(new SqlUserRepository(failing, NullLogger<SqlUserRepository>.Instance));
            var act = async () => await service.RenameAsync(new UserId("u1"), "alice");
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*injected*");
        }
        await AssertUserStateAsync(scratch, "u1", username: "alice.smith", localLink: "alice.smith", currentLedger: "alice.smith");

        // The owner reclaims: the historical row is reactivated, not re-inserted.
        var assignedBefore = await ScalarAsync<DateTimeOffset>(scratch.ConnectionString, "SELECT AssignedAtUtc FROM UsernameLedger WHERE Username = N'alice';");
        await using (var db = scratch.NewDb())
        {
            var service = new UsernameService(new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance));
            var reclaimed = await service.RenameWithResultAsync(new UserId("u1"), "alice");
            reclaimed.Outcome.Should().Be(UsernameChangeOutcome.Reclaimed);
            reclaimed.PreviousUsername.Should().Be("alice.smith");
            reclaimed.Username.Should().Be("alice");
        }
        await AssertUserStateAsync(scratch, "u1", username: "alice", localLink: "alice", currentLedger: "alice");
        (await ScalarAsync<DateTimeOffset>(scratch.ConnectionString, "SELECT AssignedAtUtc FROM UsernameLedger WHERE Username = N'alice';"))
            .Should().Be(assignedBefore, "the first assignment time is kept");
        (await QueryAsync(scratch.ConnectionString, "SELECT Username, UserId FROM UsernameLedger;"))
            .Should().BeEquivalentTo(new[] { ("alice", "u1"), ("alice.smith", "u1"), ("bob", "u2") }, "no ledger owner ever changed");
        _output.WriteLine("EXECUTED against SQL Server catalog " + scratch.Catalog);
    }

    [Fact]
    public async Task Recovery_access_canonicalization_uses_the_same_rename_and_refuses_a_name_owned_by_someone_else()
    {
        await using var scratch = await CreateMigratedAsync("Apperture_Iam9Recovery_");
        if (scratch is null)
            return;

        await AddLocalUserAsync(scratch, "u_admin", "pat.admin");
        await ExecuteAsync(scratch.ConnectionString, "UPDATE Users SET IsBreakGlassEligible = 1 WHERE Id = N'u_admin';");

        await using (var db = scratch.NewDb())
        {
            var repository = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
            var designated = (await repository.FindDesignatedEmergencyUserAsync())!;
            (await new UsernameService(repository).ApplyRecoveryAccessIdentityAsync(designated)).Should().Be(UsernameChangeOutcome.Assigned);

            var after = (await repository.GetByIdAsync(new UserId("u_admin")))!;
            EmergencyAccessIdentity.HasCanonicalIdentity(after).Should().BeTrue();
        }

        await AssertUserStateAsync(scratch, "u_admin", username: "breakglass", localLink: "breakglass", currentLedger: "breakglass");
        (await ScalarAsync<string>(scratch.ConnectionString, "SELECT CONCAT(UserId, N'|', RetiredReason) FROM UsernameLedger WHERE Username = N'pat.admin';"))
            .Should().Be("u_admin|canonicalization");

        // Revoke keeps breakglass on the same row; a later designee cannot take it.
        await ExecuteAsync(scratch.ConnectionString, "UPDATE Users SET IsBreakGlassEligible = 0 WHERE Id = N'u_admin';");
        await AddLocalUserAsync(scratch, "u_next", "next.admin");
        await ExecuteAsync(scratch.ConnectionString, "UPDATE Users SET IsBreakGlassEligible = 1 WHERE Id = N'u_next';");
        await using (var db = scratch.NewDb())
        {
            var repository = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
            var designated = (await repository.FindDesignatedEmergencyUserAsync())!;
            var act = async () => await new UsernameService(repository).ApplyRecoveryAccessIdentityAsync(designated);
            (await act.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(UsernameErrorCodes.Taken);
        }
        await AssertUserStateAsync(scratch, "u_next", username: "next.admin", localLink: "next.admin", currentLedger: "next.admin");
        _output.WriteLine("EXECUTED against SQL Server catalog " + scratch.Catalog);
    }

    // -------------------------------------------------------------------------------------------
    // Seed data and helpers
    // -------------------------------------------------------------------------------------------

    /// <summary>A populated pre-upgrade database (schema at <see cref="PreUsernameMigration"/>).</summary>
    private static string PopulatedSeedSql =>
        // Conforming Local user.
        LegacyUserSql("u_local", "Alice Adams", false) + LegacyLinkSql("l_local", "u_local", 1, "alice", null, null, null, "2026-01-01") +
        // Grandfathered email-shaped Local user, stored with upper case: lower-cased, otherwise verbatim.
        LegacyUserSql("u_email", "Jane", false) + LegacyLinkSql("l_email", "u_email", 1, "Jane@Contoso.com", null, null, null, "2026-01-02") +
        // Mixed Local and Entra: the Local name wins.
        LegacyUserSql("u_mixed", "Dave", false) + LegacyLinkSql("l_mixed", "u_mixed", 1, "dave", null, null, null, "2026-01-03") +
        LegacyLinkSql("e_mixed", "u_mixed", 0, "oid-mixed", "tenant-1", "david@contoso.com", null, "2026-01-03") +
        // Recovery Access.
        LegacyUserSql("u_ra", "Recovery Access", true) + LegacyLinkSql("l_ra", "u_ra", 1, "breakglass", null, null, null, "2026-01-04") +
        // A person who already holds a now-reserved name keeps it.
        LegacyUserSql("u_system", "System Person", false) + LegacyLinkSql("l_system", "u_system", 1, "system", null, null, null, "2026-01-05") +
        // Entra-only with a useful UPN snapshot.
        LegacyUserSql("u_entra_upn", "Bob Jones", false) + LegacyLinkSql("e_upn", "u_entra_upn", 0, "oid-upn", "tenant-1", "Bob.Jones@contoso.com", null, "2026-02-01") +
        // Entra-only, no link snapshots, provisioned from an access request that has them.
        LegacyUserSql("u_entra_req", "Microsoft user", false) + LegacyLinkSql("e_req", "u_entra_req", 0, "oid-req", "tenant-1", null, null, "2026-02-02") +
        """
        INSERT INTO EntraAccessRequests (Id, RegistrationId, TenantId, ProviderObjectId, DisplayNameSnapshot, UserPrincipalNameSnapshot, MailSnapshot, AuthenticatedAtUtc, RequestedAtUtc, Status, ProvisionedUserId)
        VALUES (N'req1', N'reg1', N'tenant-1', N'oid-req', NULL, N'carol@contoso.com', NULL, '2026-02-02', '2026-02-02', 1, N'u_entra_req');
        """ +
        // Entra-only with nothing but a display name.
        LegacyUserSql("u_entra_none", "Microsoft user", false) + LegacyLinkSql("e_none", "u_entra_none", 0, "oid-none", "tenant-1", null, null, "2026-02-03") +
        // Two configured bootstrap Entra admins: default display text is never used.
        LegacyUserSql("u_boot1", "Bootstrap administrator (11111111-1111-1111-1111-111111111111)", false) +
        LegacyLinkSql("e_boot1", "u_boot1", 0, "11111111-1111-1111-1111-111111111111", "tenant-1", null, null, "2026-02-04") +
        LegacyUserSql("u_boot2", "Bootstrap administrator (22222222-2222-2222-2222-222222222222)", false) +
        LegacyLinkSql("e_boot2", "u_boot2", 0, "22222222-2222-2222-2222-222222222222", "tenant-1", null, null, "2026-02-05") +
        // Entra-only whose generated base collides with a Local user.
        LegacyUserSql("u_entra_collide", "Alice from Entra", false) + LegacyLinkSql("e_collide", "u_entra_collide", 0, "oid-collide", "tenant-1", "alice@contoso.com", null, "2026-02-06") +
        // Canonicalization history: one safe previous name, one now held by another user, one ambiguous,
        // one written with a non-canonical (mixed-case) name, and one that cannot be read at all.
        """
        INSERT INTO AuditLogs (At, Actor, Action, Details) VALUES
          ('2026-01-10', N'emergency-cli', N'emergency.access.canonicalized', N'userId=u_ra previousUsername=pat.admin; Resource=ResourceDescriptor { ResourceType = user, ScopeKey = instance }'),
          ('2026-01-11', N'emergency-cli', N'emergency.access.canonicalized', N'userId=u_ra previousUsername=alice'),
          ('2026-01-12', N'emergency-cli', N'emergency.access.canonicalized', N'userId=u_ra previousUsername=shared.name'),
          ('2026-01-13', N'emergency-cli', N'emergency.access.canonicalized', N'userId=u_local previousUsername=shared.name'),
          ('2026-01-14', N'emergency-cli', N'emergency.access.canonicalized', N'userId=u_ra previousUsername=breakglass'),
          ('2026-01-15', N'emergency-cli', N'emergency.access.canonicalized', N'userId=u_ra previousUsername=Mixed.Case'),
          ('2026-01-16', N'emergency-cli', N'emergency.access.canonicalized', N'canonical identity applied');
        """;

    private static string LegacyUserSql(string id, string displayName, bool recoveryAccess) =>
        $"INSERT INTO Users (Id, DisplayName, IsBreakGlassEligible) VALUES (N'{id}', N'{displayName.Replace("'", "''")}', {(recoveryAccess ? 1 : 0)});\n";

    private static string LegacyLinkSql(
        string id, string userId, int source, string externalId, string? tenantId, string? upn, string? mail, string linkedAt = "2026-01-01") =>
        $"""
        INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, TenantId, LinkedAt, UserPrincipalNameSnapshot, MailSnapshot)
        VALUES (N'{id}', N'{userId}', {source}, N'{externalId}', {Sql(tenantId)}, '{linkedAt}', {Sql(upn)}, {Sql(mail)});

        """;

    private static string InsertUserSql(string id, string username) =>
        $"INSERT INTO Users (Id, Username, DisplayName, IsBreakGlassEligible) VALUES (N'{id}', N'{username}', N'{id}', 0);\n";

    private static string InsertLedgerSql(string username, string userId, bool retired) =>
        retired
            ? $"INSERT INTO UsernameLedger (Username, UserId, AssignedAtUtc, RetiredAtUtc, RetiredReason) VALUES (N'{username}', N'{userId}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'rename');\n"
            : $"INSERT INTO UsernameLedger (Username, UserId, AssignedAtUtc) VALUES (N'{username}', N'{userId}', SYSDATETIMEOFFSET());\n";

    private static string InsertLinkSql(string id, string userId, int source, string externalId, string? tenantId) =>
        $"INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, TenantId, LinkedAt) VALUES (N'{id}', N'{userId}', {source}, N'{externalId}', {Sql(tenantId)}, SYSDATETIMEOFFSET());\n";

    private static string Sql(string? value) => value is null ? "NULL" : $"N'{value}'";

    private static async Task AddLocalUserAsync(Scratch scratch, string id, string username)
    {
        await using var db = scratch.NewDb();
        await new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance).AddAsync(
            new User(new UserId(id), id, [new IdentityLink(IdentitySource.Local, username, null)], [], false) { Username = username });
    }

    private static async Task AssertUserStateAsync(Scratch scratch, string userId, string username, string localLink, string currentLedger)
    {
        (await ScalarAsync<string>(scratch.ConnectionString, $"SELECT Username FROM Users WHERE Id = N'{userId}';")).Should().Be(username);
        (await ScalarAsync<string>(scratch.ConnectionString, $"SELECT ExternalId FROM IdentityLinks WHERE UserId = N'{userId}' AND Source = 1;")).Should().Be(localLink);
        (await ScalarAsync<string>(scratch.ConnectionString, $"SELECT Username FROM UsernameLedger WHERE UserId = N'{userId}' AND RetiredAtUtc IS NULL;")).Should().Be(currentLedger);
    }

    /// <summary>Throws on SaveChanges: the point after the ledger UPDATEs and before Users and the Local link are written.</summary>
    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fault injected between the ledger and the Users/Local-link writes.");
    }

    private async Task<Scratch?> CreateMigratedAsync(string prefix)
    {
        var scratch = await CreateEmptyAsync(prefix);
        if (scratch is null)
            return null;
        await using (var db = scratch.NewDb())
            await AppertureDatabaseMigrator.MigrateAsync(db);
        return scratch;
    }

    private async Task<Scratch?> CreateEmptyAsync(string prefix)
    {
        var baseConnectionString = SqlTestExecution.ConnectionStringOrNotExecuted();
        if (baseConnectionString is null)
            return Announce("no SQL Server endpoint is configured");

        var catalog = prefix + Guid.NewGuid().ToString("N")[..12];
        var connectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;
        try
        {
            SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);
        }
        catch (InvalidOperationException ex)
        {
            return NotExecuted("the SQL Server endpoint is unreachable: " + ex.Message);
        }

        return new Scratch(baseConnectionString, catalog, connectionString);
    }

    /// <summary>Declares the skip (throws when SQL Server proof is required) and also writes it to this test's output.</summary>
    private Scratch? NotExecuted(string reason)
    {
        SqlTestExecution.NotExecuted(reason);
        return Announce(reason);
    }

    /// <summary>Writes a skip that <see cref="SqlTestExecution"/> has already declared to this test's output.</summary>
    private Scratch? Announce(string reason)
    {
        _output.WriteLine("NOT EXECUTED (soft skip): " + reason);
        return null;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<(string, string)>> QueryAsync(string connectionString, string sql)
    {
        var rows = new List<(string, string)>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));
        return rows;
    }

    private sealed class Scratch(string baseConnectionString, string catalog, string connectionString) : IAsyncDisposable
    {
        public string Catalog { get; } = catalog;
        public string ConnectionString { get; } = connectionString;

        public AppertureDbContext NewDb(params IInterceptor[] interceptors) =>
            new(new DbContextOptionsBuilder<AppertureDbContext>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure())
                .AddInterceptors(interceptors)
                .Options);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecuteAsync(
                    new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" }.ConnectionString,
                    $"""
                    IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{Catalog}')
                    BEGIN
                        ALTER DATABASE [{Catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                        DROP DATABASE [{Catalog}];
                    END
                    """);
            }
            catch
            {
                // Cleanup is best-effort; a leftover throwaway catalog must not fail the run.
            }
        }
    }
}
