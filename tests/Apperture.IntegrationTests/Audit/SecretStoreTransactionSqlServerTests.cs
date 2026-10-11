using System.Collections.Concurrent;
using System.Data.Common;
using Apperture.Core.Application.Audit;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 secret-store transaction participation (design gate §13.4 and its §22 ruling on the secret store) against SQL
/// Server. Outside a transaction each store change commits on its own with its <c>SecretAudits</c> row. Inside one, such as
/// an audited unit's, the store joins it and never begins, commits or retries a transaction of its own, so the secret change,
/// its <c>SecretAudits</c> row and the owner's audit row commit together or not at all. Fails, never skips, without SQL
/// Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SecretStoreTransactionSqlServerTests : IAsyncLifetime
{
    /// <summary>A value shaped like a real credential. It must never leave the secret store's own payload column.</summary>
    private static readonly string Sentinel = "sentinel-secret-" + Guid.NewGuid().ToString("N");

    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public SecretStoreTransactionSqlServerTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync() => _db = await AuditSqlScratch.CreateAsync("Apperture_Aud14SecretStore_");

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    // ---- Standalone: no transaction is open ----

    [Fact]
    public async Task Outside_a_transaction_each_change_commits_on_its_own_with_its_store_evidence()
    {
        Guid id;
        string first, second;
        await using (var db = _db.NewDb())
        {
            var store = Store(db);
            var descriptor = await store.CreateAsync(Request("owner:standalone"));
            id = descriptor.SecretId.Value;
            first = await store.AddVersionAsync(descriptor.SecretId, Material(Sentinel));
            await store.ActivateVersionAsync(descriptor.SecretId, first);
            (await ResolveAsync(id)).Should().Be(Sentinel);

            second = await store.AddVersionAsync(descriptor.SecretId, Material("second-" + Sentinel));
            (await ResolveAsync(id)).Should().Be(Sentinel, "a pending version is not resolved");
            await store.ActivateVersionAsync(descriptor.SecretId, second);
            (await ResolveAsync(id)).Should().Be("second-" + Sentinel);

            await store.RevokeVersionAsync(descriptor.SecretId, first);
            await store.DisableAsync(descriptor.SecretId);
        }

        (await EventsAsync(id)).Should().Equal("Create", "AddVersion", "ActivateVersion", "AddVersion", "ActivateVersion", "RevokeVersion", "Disable");
        (await _db.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{id}';")).Should().Be((int)SecretState.Disabled);
        (await _db.ScalarAsync<int>($"SELECT State FROM SecretVersions WHERE SecretId = '{id}' AND Version = N'{first}';")).Should().Be((int)SecretState.Revoked);
        (await ResolveAsync(id)).Should().BeNull("a disabled secret never resolves");
        await AssertSentinelOnlyInPayloadsAsync();
    }

    [Fact]
    public async Task Outside_a_transaction_a_refused_activation_changes_nothing()
    {
        // A validation refusal: it throws before the store modifies anything, so this shows the refusal, not the cleanup of a
        // staged change (that is the next test).
        await using var db = _db.NewDb();
        var store = Store(db);
        var descriptor = await store.CreateAsync(Request("owner:refused"));
        var version = await store.AddVersionAsync(descriptor.SecretId, Material(Sentinel));
        await store.ActivateVersionAsync(descriptor.SecretId, version);

        var again = () => store.ActivateVersionAsync(descriptor.SecretId, version).AsTask();
        await again.Should().ThrowAsync<InvalidOperationException>("only a pending version can be activated");

        await db.SaveChangesAsync();
        (await EventsAsync(descriptor.SecretId.Value)).Should().Equal("Create", "AddVersion", "ActivateVersion");
    }

    [Fact]
    public async Task Outside_a_transaction_a_save_that_fails_after_staging_persists_nothing_and_leaves_no_staged_entry_to_save_later()
    {
        var (id, first) = await ArrangeLiveSecretAsync("owner:save-fails", Sentinel);
        var eventsBefore = await EventsAsync(id);
        var updatedBefore = await _db.ScalarAsync<DateTimeOffset>($"SELECT UpdatedAt FROM SecretDescriptors WHERE Id = '{id}';");

        // The first statement that inserts the store's evidence row fails, with the version, the descriptor change and the
        // evidence row all staged on the context. The interceptor is then spent, so a later save succeeds.
        var failure = new FailOnceAtCommand(sql => sql.Contains("INSERT INTO [SecretAudits]", StringComparison.Ordinal));
        var attempts = new AttemptCounter();
        await using var db = _db.NewDb(failure, attempts);
        failure.Context = db;
        var store = Store(db);

        var act = () => store.AddVersionAsync(new SecretId(id), Material("replacement-" + Sentinel)).AsTask();
        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;

        failure.Failures.Should().Be(1);
        attempts.Started.Should().Be(1, "an injected failure that is not transient is not rerun");
        failure.StagedWhenFailed.Should().Contain(new[]
        {
            (nameof(SecretVersionEntity), EntityState.Added),
            (nameof(SecretDescriptorEntity), EntityState.Modified),
            (nameof(SecretAuditEntity), EntityState.Added),
        }, "the failure came after a meaningful change was staged");

        // Atomic: nothing of the change or its evidence is in the database.
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{id}';")).Should().Be(1);
        (await _db.ScalarAsync<string>($"SELECT ActiveVersion FROM SecretDescriptors WHERE Id = '{id}';")).Should().Be(first);
        (await _db.ScalarAsync<DateTimeOffset>($"SELECT UpdatedAt FROM SecretDescriptors WHERE Id = '{id}';")).Should().Be(updatedBefore);
        (await EventsAsync(id)).Should().Equal(eventsBefore);

        // Clean: nothing the failed operation staged is still tracked, so nothing can be saved by accident.
        db.ChangeTracker.Entries().Should().NotContain(e => e.State != EntityState.Unchanged);
        (await db.SaveChangesAsync()).Should().Be(0, "the failed change is not pending");
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{id}';")).Should().Be(1);
        (await EventsAsync(id)).Should().Equal(eventsBefore);

        // A later operation on the same context persists only itself.
        await store.AddVersionAsync(new SecretId(id), Material("second-" + Sentinel));
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{id}';")).Should().Be(2, "the failed version never existed");
        (await EventsAsync(id)).Should().Equal([.. eventsBefore, "AddVersion"]);

        AllMessages(thrown).Should().NotContain(m => m.Contains(Sentinel, StringComparison.Ordinal));
        AssertNoReference(AllMessages(thrown), id);
        await AssertSentinelOnlyInPayloadsAsync();
    }

    [Fact]
    public async Task Outside_a_transaction_a_failed_commit_leaves_no_trace_and_does_not_discard_or_accept_other_pending_changes()
    {
        var (id, _) = await ArrangeLiveSecretAsync("owner:commit-fails", Sentinel);
        var (otherId, _) = await ArrangeLiveSecretAsync("owner:other", "other-" + Sentinel);
        var eventsBefore = await EventsAsync(id);

        // Every statement runs in the store's transaction, then the server rolls the commit back.
        await using var db = _db.NewDb(new RolledBackCommit());
        var store = Store(db);
        var other = (await db.SecretDescriptors.FindAsync(otherId))!;
        other.Name = "owner:renamed-by-the-caller";

        var act = () => store.DisableAsync(new SecretId(id)).AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>("the injected commit failure propagates unchanged");

        (await _db.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{id}';")).Should().Be((int)SecretState.Active);
        (await EventsAsync(id)).Should().Equal(eventsBefore);
        (await _db.ScalarAsync<string>($"SELECT Name FROM SecretDescriptors WHERE Id = '{otherId}';")).Should().Be("owner:other");
        db.ChangeTracker.Entries<SecretDescriptorEntity>().Should().NotContain(e => e.Entity.Id == id, "what the store itself changed is no longer tracked");

        // The caller's own change is still pending, because the save that carried it was never confirmed.
        db.Entry(other).State.Should().Be(EntityState.Modified, "a failed commit neither discards nor accepts changes it did not make");
        await db.SaveChangesAsync();
        (await _db.ScalarAsync<string>($"SELECT Name FROM SecretDescriptors WHERE Id = '{otherId}';")).Should().Be("owner:renamed-by-the-caller");
        (await _db.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{id}';")).Should().Be((int)SecretState.Active, "the store's own failed change did not come back with it");
        (await EventsAsync(id)).Should().Equal(eventsBefore);
    }

    [Fact]
    public async Task Outside_a_transaction_a_lost_commit_acknowledgement_is_not_reported_as_a_rollback_and_is_not_rerun()
    {
        // The commit reaches the server and the acknowledgement is lost. Outside an audited unit nothing verifies it, so the
        // store reports the original failure, never "nothing changed", and never reruns the create.
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        await using var db = _db.NewDb(lost, attempts);
        var store = Store(db);

        var act = () => store.CreateAsync(Request("owner:lost-ack")).AsTask();
        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;

        thrown.Message.Should().Contain("Injected", "the original failure is reported as it is");
        attempts.Started.Should().Be(1);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretDescriptors WHERE Name = N'owner:lost-ack';")).Should().Be(1, "the change did commit");

        db.ChangeTracker.Entries().Should().NotContain(e => e.State != EntityState.Unchanged, "nothing is left that a later save could insert again");
        await db.SaveChangesAsync();
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretDescriptors WHERE Name = N'owner:lost-ack';")).Should().Be(1);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretAudits;")).Should().Be(1);
    }

    // ---- Joined: a transaction is already open on the context ----
    // As in production, the caller's transaction is opened inside the execution strategy: with retry on failure, EF refuses
    // a caller transaction opened outside it, for any save.

    [Fact]
    public async Task Inside_an_open_transaction_the_store_joins_it_and_rolls_back_with_it()
    {
        var transactions = new AttemptCounter();
        await using (var db = _db.NewDb(transactions))
        {
            var store = Store(db);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                var descriptor = await store.CreateAsync(Request("owner:joined"));
                var version = await store.AddVersionAsync(descriptor.SecretId, Material(Sentinel));
                await store.ActivateVersionAsync(descriptor.SecretId, version);
                await store.DisableAsync(descriptor.SecretId);

                db.Database.CurrentTransaction.Should().BeSameAs(transaction, "the store neither committed nor replaced the caller's transaction");
                await transaction.RollbackAsync();
            });
        }

        transactions.Started.Should().Be(1, "no store operation began a transaction of its own inside the caller's");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretDescriptors WHERE Name = N'owner:joined';")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretVersions;")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM SecretAudits;")).Should().Be(0, "the store's evidence rolled back with the change it describes");
    }

    [Fact]
    public async Task Inside_an_open_transaction_the_stores_changes_commit_only_with_the_callers_commit()
    {
        var id = Guid.Empty;
        var transactions = new AttemptCounter();
        await using (var db = _db.NewDb(transactions))
        {
            var store = Store(db);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                var descriptor = await store.CreateAsync(Request("owner:joined-commit"));
                id = descriptor.SecretId.Value;
                var version = await store.AddVersionAsync(descriptor.SecretId, Material(Sentinel));
                await store.ActivateVersionAsync(descriptor.SecretId, version);

                // From another connection, skipping locked rows: nothing is committed before the caller commits.
                (await ScalarWithTimeoutAsync($"SELECT COUNT(*) FROM SecretDescriptors WITH (READPAST) WHERE Id = '{id}';")).Should().Be(0);
                (await ScalarWithTimeoutAsync("SELECT COUNT(*) FROM SecretAudits WITH (READPAST);")).Should().Be(0);
                await transaction.CommitAsync();
            });
        }

        transactions.Started.Should().Be(1);
        (await ResolveAsync(id)).Should().Be(Sentinel);
        (await EventsAsync(id)).Should().Equal("Create", "AddVersion", "ActivateVersion");
    }

    // ---- Inside an audited unit ----

    [Fact]
    public async Task In_an_audited_unit_the_secret_change_its_store_evidence_and_the_audit_row_commit_together()
    {
        var (admin, principal) = await AdminAsync();
        Guid id = Guid.Empty;
        var attempts = new AttemptCounter();
        await using (var db = _db.NewDb(attempts))
        {
            var store = Store(db);
            await NewUnit(db, principal).ExecuteAsync(
                new AuditedOperation(HarnessPrimary.Action, AuditActor.CurrentPrincipal),
                async (transaction, ct) =>
                {
                    var descriptor = await store.CreateAsync(Request("owner:unit"), ct);
                    id = descriptor.SecretId.Value;
                    var version = await store.AddVersionAsync(descriptor.SecretId, Material(Sentinel), ct);
                    await store.ActivateVersionAsync(descriptor.SecretId, version, ct);
                    await transaction.StageAsync(HarnessEvent(HarnessPrimary, "owner-unit"), ct);
                    return true;
                },
                (_, _) => Task.FromResult(true));
        }

        attempts.Started.Should().Be(1, "the unit's transaction is the only one");
        (await ResolveAsync(id)).Should().Be(Sentinel);
        (await EventsAsync(id)).Should().Equal("Create", "AddVersion", "ActivateVersion");
        var row = (await _db.RowsAsync("SELECT * FROM AuditLogs;")).Should().ContainSingle().Which;
        row["Actor"].Should().Be(admin);
        await AssertSentinelOnlyInPayloadsAsync();
        await AssertNoSecretIdInAuditLogsAsync(id);
    }

    [Fact]
    public async Task When_the_audit_insert_fails_the_new_version_is_neither_stored_nor_live_and_the_prior_secret_resolves()
    {
        var (_, principal) = await AdminAsync();
        var (id, first) = await ArrangeLiveSecretAsync("owner:audit-fails", Sentinel);
        var eventsBefore = await EventsAsync(id);

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        Exception? thrown;
        await using (var db = _db.NewDb(failAudit))
        {
            var store = Store(db);
            var act = () => NewUnit(db, principal).ExecuteAsync(
                new AuditedOperation(HarnessPrimary.Action, AuditActor.CurrentPrincipal),
                async (transaction, ct) =>
                {
                    var version = await store.AddVersionAsync(new SecretId(id), Material("replacement-" + Sentinel), ct);
                    await store.ActivateVersionAsync(new SecretId(id), version, ct);
                    await transaction.StageAsync(HarnessEvent(HarnessPrimary, "owner-audit-fails"), ct);
                    return true;
                },
                (_, _) => Task.FromResult(true));
            thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        }

        failAudit.Failures.Should().BeGreaterThan(0);
        (await ResolveAsync(id)).Should().Be(Sentinel, "the live credential never changed");
        (await _db.ScalarAsync<string>($"SELECT ActiveVersion FROM SecretDescriptors WHERE Id = '{id}';")).Should().Be(first);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{id}';")).Should().Be(1, "the replacement version rolled back");
        (await EventsAsync(id)).Should().Equal(eventsBefore, "no store evidence for a change that did not commit");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(0);
        AllMessages(thrown!).Should().NotContain(m => m.Contains(Sentinel, StringComparison.Ordinal));
        AssertNoReference(AllMessages(thrown!), id);
        await AssertSentinelOnlyInPayloadsAsync();
        await AssertNoSecretIdInAuditLogsAsync(id);
    }

    [Fact]
    public async Task A_store_on_another_context_does_not_join_the_unit_which_is_why_the_hosts_give_both_one_context()
    {
        // The negative case of the join: the store joins only a transaction open on its own context. On a different context
        // its change commits on its own and survives the unit's rollback. SecretStoreSharesTheUnitContextTests pins that the
        // hosts' registrations give the store and the unit one context per scope.
        var (_, principal) = await AdminAsync();
        var (id, _) = await ArrangeLiveSecretAsync("owner:other-context", Sentinel);

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        await using (var other = _db.NewDb())
        {
            var store = Store(other);
            var act = () => NewUnit(db, principal).ExecuteAsync(
                new AuditedOperation(HarnessPrimary.Action, AuditActor.CurrentPrincipal),
                async (transaction, ct) =>
                {
                    var version = await store.AddVersionAsync(new SecretId(id), Material("escaped-" + Sentinel), ct);
                    await store.ActivateVersionAsync(new SecretId(id), version, ct);
                    await transaction.StageAsync(HarnessEvent(HarnessPrimary, "owner-other-context"), ct);
                    return true;
                },
                (_, _) => Task.FromResult(true));
            await act.Should().ThrowAsync<Exception>();
        }

        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(0);
        (await ResolveAsync(id)).Should().Be("escaped-" + Sentinel, "a store on another context is not part of the unit");
    }

    [Fact]
    public async Task A_rerun_after_a_confirmed_rollback_activates_only_its_own_version_and_records_it_once()
    {
        var (_, principal) = await AdminAsync();
        var (id, first) = await ArrangeLiveSecretAsync("owner:rerun", Sentinel);
        var eventsBefore = await EventsAsync(id);

        // The first commit is rolled back by the server; verification confirms it and the unit reruns once.
        var rolledBack = new RolledBackCommit();
        var attempts = new AttemptCounter();
        var versions = new List<string>();
        await using (var db = _db.NewDb(rolledBack, attempts))
        {
            var store = Store(db);
            await NewUnit(db, principal).ExecuteAsync(
                new AuditedOperation(HarnessPrimary.Action, AuditActor.CurrentPrincipal),
                async (transaction, ct) =>
                {
                    var version = await store.AddVersionAsync(new SecretId(id), Material("replacement-" + Sentinel), ct);
                    versions.Add(version);
                    await store.ActivateVersionAsync(new SecretId(id), version, ct);
                    await transaction.StageAsync(HarnessEvent(HarnessPrimary, "owner-rerun"), ct);
                    return true;
                },
                (_, _) => Task.FromResult(true));
        }

        attempts.Started.Should().Be(2);
        versions.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        var stored = (await _db.RowsAsync($"SELECT Version, State FROM SecretVersions WHERE SecretId = '{id}';"))
            .Select(r => ((string)r["Version"]!, (SecretState)(int)r["State"]!)).ToList();
        stored.Should().BeEquivalentTo(new[] { (first, SecretState.Active), (versions[1], SecretState.Active) },
            "the rolled-back attempt's version does not exist, and the rerun's is the live one");
        (await _db.ScalarAsync<string>($"SELECT ActiveVersion FROM SecretDescriptors WHERE Id = '{id}';")).Should().Be(versions[1]);
        (await ResolveAsync(id)).Should().Be("replacement-" + Sentinel);
        (await EventsAsync(id)).Should().Equal([.. eventsBefore, "AddVersion", "ActivateVersion"]);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(1);
    }

    [Fact]
    public async Task A_lost_acknowledgement_is_verified_without_a_rerun_and_the_change_is_recorded_once()
    {
        var (_, principal) = await AdminAsync();
        var (id, _) = await ArrangeLiveSecretAsync("owner:lost", Sentinel);

        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        await using (var db = _db.NewDb(lost, attempts))
        {
            var store = Store(db);
            (await NewUnit(db, principal).ExecuteAsync(
                new AuditedOperation(HarnessPrimary.Action, AuditActor.CurrentPrincipal),
                async (transaction, ct) =>
                {
                    var version = await store.AddVersionAsync(new SecretId(id), Material("replacement-" + Sentinel), ct);
                    await store.ActivateVersionAsync(new SecretId(id), version, ct);
                    await transaction.StageAsync(HarnessEvent(HarnessPrimary, "owner-lost"), ct);
                    return "attempt";
                },
                (_, _) => Task.FromResult("reconstructed"))).Should().Be("reconstructed");
        }

        attempts.Started.Should().Be(1);
        (await ResolveAsync(id)).Should().Be("replacement-" + Sentinel);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{id}';")).Should().Be(2);
        (await EventsAsync(id)).Count(e => e == "ActivateVersion").Should().Be(2, "one for the arranged version, one for the verified change");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unknown_outcome_leaves_the_secret_change_and_its_audit_row_together_committed_or_together_absent(bool committed)
    {
        var (_, principal) = await AdminAsync();
        var (id, first) = await ArrangeLiveSecretAsync("owner:unknown", Sentinel);

        Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor fault = committed ? new LostCommitAcknowledgement() : new RolledBackCommit();
        var logs = new CapturingLogger<SqlAuditedUnitOfWork>();
        Exception? thrown;
        await using (var db = _db.NewDb(fault))
        {
            var store = Store(db);
            var unit = new SqlAuditedUnitOfWork(db, principal, HarnessCatalog, TimeProvider.System, FastOptions(), logs, null, new UnreachableVerification());
            var act = () => unit.ExecuteAsync(
                new AuditedOperation(HarnessPrimary.Action, AuditActor.CurrentPrincipal),
                async (transaction, ct) =>
                {
                    var version = await store.AddVersionAsync(new SecretId(id), Material("replacement-" + Sentinel), ct);
                    await store.ActivateVersionAsync(new SecretId(id), version, ct);
                    await transaction.StageAsync(HarnessEvent(HarnessPrimary, "owner-unknown"), ct);
                    return true;
                },
                (_, _) => Task.FromResult(true));
            thrown = (await act.Should().ThrowAsync<AuditOutcomeUnknownException>()).Which;
        }

        var auditRows = await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");
        var versionCount = await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{id}';");
        var active = await _db.ScalarAsync<string>($"SELECT ActiveVersion FROM SecretDescriptors WHERE Id = '{id}';");
        if (committed)
        {
            auditRows.Should().Be(1);
            versionCount.Should().Be(2);
            active.Should().NotBe(first);
        }
        else
        {
            auditRows.Should().Be(0);
            versionCount.Should().Be(1);
            active.Should().Be(first);
        }

        logs.Messages.Should().NotContain(m => m.Contains(Sentinel, StringComparison.Ordinal));
        AllMessages(thrown!).Should().NotContain(m => m.Contains(Sentinel, StringComparison.Ordinal));
        AssertNoReference(logs.Messages, id);
        AssertNoReference(AllMessages(thrown!), id);
        await AssertSentinelOnlyInPayloadsAsync();
        await AssertNoSecretIdInAuditLogsAsync(id);
    }

    // ---- helpers ----

    private async Task<(string Admin, Principal Principal)> AdminAsync()
    {
        var id = NewUserId();
        await _db.AddUserAsync(id, NewUsername("admin"));
        return (id, new Principal(id));
    }

    /// <summary>A secret whose first version holds <paramref name="plaintext"/> and is live, committed on its own.</summary>
    private async Task<(Guid Id, string Version)> ArrangeLiveSecretAsync(string name, string plaintext)
    {
        await using var db = _db.NewDb();
        var store = Store(db);
        var descriptor = await store.CreateAsync(Request(name));
        var version = await store.AddVersionAsync(descriptor.SecretId, Material(plaintext));
        await store.ActivateVersionAsync(descriptor.SecretId, version);
        return (descriptor.SecretId.Value, version);
    }

    private async Task<string?> ResolveAsync(Guid id)
    {
        await using var db = _db.NewDb();
        try
        {
            var resolved = await new SqlSecretResolver(Store(db), new PassThroughProtector()).ResolveAsync(new SecretRef(id.ToString("D")));
            return resolved.Material.OpaqueString;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<List<string>> EventsAsync(Guid id) =>
        (await _db.RowsAsync($"SELECT EventType FROM SecretAudits WHERE SecretId = '{id}' ORDER BY Id;"))
            .Select(r => (string)r["EventType"]!).ToList();

    private async Task<int> ScalarWithTimeoutAsync(string sql)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 5;
        return (int)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// The sentinel exists only in the store's own protected payload (a pass-through protector here): never in an audit
    /// log column or in the store's evidence rows.
    /// </summary>
    private async Task AssertSentinelOnlyInPayloadsAsync()
    {
        foreach (var row in await _db.RowsAsync("SELECT * FROM AuditLogs;"))
            row.Values.OfType<string>().Should().NotContain(v => v.Contains(Sentinel, StringComparison.Ordinal));
        foreach (var row in await _db.RowsAsync("SELECT * FROM SecretAudits;"))
            row.Values.Select(v => v?.ToString() ?? "").Should().NotContain(v => v.Contains(Sentinel, StringComparison.Ordinal));
        foreach (var row in await _db.RowsAsync("SELECT Name, Provider FROM SecretDescriptors;"))
            row.Values.OfType<string>().Should().NotContain(v => v.Contains(Sentinel, StringComparison.Ordinal));
    }

    /// <summary>
    /// A store id is the reference its owner holds. The audit log never records it, in either GUID form (gate §12.3, §22). The
    /// store's own evidence is keyed by it, but no other column of that evidence holds it.
    /// </summary>
    private async Task AssertNoSecretIdInAuditLogsAsync(Guid id)
    {
        foreach (var row in await _db.RowsAsync("SELECT * FROM AuditLogs;"))
            AssertNoReference(row.Values.OfType<string>(), id);

        foreach (var row in await _db.RowsAsync("SELECT EventType, Version, Actor, Outcome FROM SecretAudits;"))
            AssertNoReference(row.Values.OfType<string>(), id);
    }

    private static void AssertNoReference(IEnumerable<string> values, Guid id)
    {
        foreach (var form in new[] { id.ToString("D"), id.ToString("N") })
            values.Should().NotContain(v => v.Contains(form, StringComparison.OrdinalIgnoreCase), $"a secret reference ({form.Length} characters) is never recorded");
    }

    /// <summary>
    /// Fails the first command whose text matches, after recording what the context's change tracker held at that moment, then
    /// lets later commands through.
    /// </summary>
    private sealed class FailOnceAtCommand(Func<string, bool> matches) : DbCommandInterceptor
    {
        public AppertureDbContext? Context { get; set; }

        public int Failures { get; private set; }

        public List<(string Entity, EntityState State)> StagedWhenFailed { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return ValueTask.FromResult(result);
        }

        private void Check(DbCommand command)
        {
            if (Failures > 0 || !matches(command.CommandText))
                return;
            Failures++;
            StagedWhenFailed.AddRange(Context!.ChangeTracker.Entries().Select(e => (e.Metadata.ClrType.Name, e.State)));
            throw new InvalidOperationException("Injected: the database refused this statement.");
        }
    }

    private static IEnumerable<string> AllMessages(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
            yield return e.Message;
    }

    private static SecretCreateRequest Request(string name) => new(name, "test", SecretMaterialType.OpaqueString);

    private static SecretMaterial Material(string plaintext) => new(SecretMaterialType.OpaqueString, OpaqueString: plaintext);

    private static SqlSecretStore Store(AppertureDbContext db) => new(db, new PassThroughProtector(), new DefaultSecretAuditActorProvider());

    private sealed class PassThroughProtector : Apperture.Core.Security.Secrets.Abstractions.ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(plaintext.ToArray());

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(protectedPayload.ToArray());
    }

    /// <summary>Captures every formatted message and exception text the unit logs.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _messages.Enqueue(formatter(state, exception));
            for (var e = exception; e is not null; e = e.InnerException)
                _messages.Enqueue(e.Message);
        }
    }
}
