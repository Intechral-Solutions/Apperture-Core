using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.ConnectionSqlHarness;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 5: connection verification (one external attempt, then one audited unit that records what the verifier answered)
/// and structure browse (a required security event committed before the result is delivered, or a best-effort attempt row when the
/// request threw) against SQL Server, on a locking READ COMMITTED catalog and on a READ_COMMITTED_SNAPSHOT catalog (design gate
/// §11.6, §13.1, §13.3, M6, M7). Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ConnectionVerifyBrowseSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string RemoteText = "remote said: token=abc123 for sentinel-remote-host.example";

    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public ConnectionVerifyBrowseSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    // ---- Verification: the committed record ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_verification_records_one_external_attempt_row_and_stores_only_the_verification_columns(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: true);

        await VerifyAsync(scratch, principal, seeded.ConnectionId, Answer(ConnectionVerificationStatus.Success));

        var row = (await AuditRowsAsync(scratch, seeded.ConnectionId)).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.ConnectionVerified);
        row["Actor"].Should().Be(admin);
        row["Category"].Should().Be(AuditCategories.Configuration);
        row["Severity"].Should().Be(AuditSeverities.Standard);
        row["Basis"].Should().Be(AuditBases.ExternalAttempt);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Succeeded);
        row["TargetType"].Should().Be(AuditTargetTypes.Connection);
        row["OperationEventId"].Should().Be(row["EventId"]);
        row["ResourceContextId"].Should().Be(ResourceContextIds.SharedValue);
        Detail(row, "verificationStatus").Should().Be("success");
        ((string)row["Details"]!).Should().NotContain("token=abc123").And.NotContain("sentinel-remote-host").And.NotContain(HostSentinel);

        var stored = await scratch.RowsAsync($"SELECT LastVerificationStatus, LastVerificationSummary FROM Connections WHERE Id = N'{seeded.ConnectionId}';");
        stored.Single()["LastVerificationStatus"].Should().Be((int)ConnectionVerificationStatus.Success);
        stored.Single()["LastVerificationSummary"].Should().Be(RemoteText, "the summary stays on the connection row, not in the audit log");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(ConnectionVerificationStatus.Failed, "failed", "failed", false)]
    [InlineData(ConnectionVerificationStatus.Unavailable, "unknown", "unavailable", false)]
    [InlineData(ConnectionVerificationStatus.Failed, "failed", "failed", true)]
    [InlineData(ConnectionVerificationStatus.Unavailable, "unknown", "unavailable", true)]
    public async Task A_failure_or_unavailable_answer_is_recorded_as_what_the_verifier_said(
        ConnectionVerificationStatus answer, string outcome, string recorded, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: false);

        await VerifyAsync(scratch, principal, seeded.ConnectionId, Answer(answer));

        var row = (await AuditRowsAsync(scratch, seeded.ConnectionId)).Should().ContainSingle().Subject;
        row["Outcome"].Should().Be(outcome);
        Detail(row, "verificationStatus").Should().Be(recorded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_verifier_that_throws_is_recorded_as_a_failed_attempt_with_no_status_and_changes_no_column(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: false);
        var before = await SnapshotAsync(scratch);

        await using (var db = scratch.NewDb())
        {
            var failure = new TimeoutException(RemoteText);
            var verification = new ScriptedVerification((_, _) => throw failure);
            var act = () => Service(db, principal, NewUnit(db, principal), verification).VerifyConnectionAsync(new ConnectionId(seeded.ConnectionId)).AsTask();
            (await act.Should().ThrowAsync<TimeoutException>()).Which.Should().BeSameAs(failure);
        }

        var row = (await AuditRowsAsync(scratch, seeded.ConnectionId)).Should().ContainSingle().Subject;
        row["Outcome"].Should().Be(AuditOutcomeCodes.Failed);
        Detail(row, "verificationStatus").Should().BeNull("the verifier produced no answer");
        ((string)row["Details"]!).Should().NotContain("token=abc123").And.NotContain("sentinel-remote-host");
        (await SnapshotAsync(scratch)).Should().Be(before, "a probe that produced nothing stores nothing on the connection");
    }

    // ---- Verification: the order of the probe and the unit ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_probe_runs_outside_any_transaction(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: false);
        bool? transactionOpen = null;

        await using (var db = scratch.NewDb())
        {
            var verification = new ScriptedVerification((_, _) =>
            {
                transactionOpen = db.Database.CurrentTransaction is not null;
                return Task.FromResult(new ConnectionVerificationResult(ConnectionVerificationStatus.Success, DateTimeOffset.UtcNow, "ok"));
            });
            await Service(db, principal, NewUnit(db, principal), verification).VerifyConnectionAsync(new ConnectionId(seeded.ConnectionId));
        }

        transactionOpen.Should().BeFalse("a transaction never makes an external effect atomic, and it would hold locks across the remote call");
    }

    public enum Meanwhile
    {
        Rename,
        ConfigChange,
        Rotation,
        Retirement,
    }

    public static IEnumerable<object[]> MeanwhileCases() =>
        from change in Enum.GetValues<Meanwhile>()
        from rcsi in new[] { false, true }
        select new object[] { change, rcsi };

    [Theory]
    [MemberData(nameof(MeanwhileCases))]
    public async Task A_change_made_while_the_probe_runs_is_kept_and_only_the_verification_columns_are_written(Meanwhile change, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: true);
        var id = new ConnectionId(seeded.ConnectionId);

        await using (var db = scratch.NewDb())
        {
            var verification = new ScriptedVerification(async (_, _) =>
            {
                // Someone else changes the connection while the remote host is being probed (a separate request).
                await using var other = scratch.NewDb();
                var service = Service(other, principal, NewUnit(other, principal));
                switch (change)
                {
                    case Meanwhile.Rename:
                        await service.UpdateConnectionAsync(id, new UpdateConnectionRequest("Renamed meanwhile", null, null));
                        break;
                    case Meanwhile.ConfigChange:
                        await service.UpdateConnectionAsync(id, new UpdateConnectionRequest(
                            null, new Dictionary<string, string> { ["Host"] = HostSentinel, ["Database"] = "Ops" }, null));
                        break;
                    case Meanwhile.Rotation:
                        await service.UpdateConnectionAsync(id, new UpdateConnectionRequest(
                            null, null, new Dictionary<string, string> { ["Password"] = NewSecret }));
                        break;
                    default:
                        await service.RetireConnectionAsync(id);
                        break;
                }

                return new ConnectionVerificationResult(ConnectionVerificationStatus.Success, DateTimeOffset.UtcNow, "ok");
            });
            await Service(db, principal, NewUnit(db, principal), verification).VerifyConnectionAsync(id);
        }

        await using var read = scratch.NewDb();
        var stored = (await new SqlConnectionRepository(read).GetByIdAsync(id))!;
        stored.LastVerificationStatus.Should().Be(ConnectionVerificationStatus.Success);
        switch (change)
        {
            case Meanwhile.Rename:
                stored.Name.Should().Be("Renamed meanwhile");
                break;
            case Meanwhile.ConfigChange:
                stored.Config.Should().ContainKey("Database").WhoseValue.Should().Be("Ops");
                break;
            case Meanwhile.Rotation:
                (await ResolveAsync(scratch, seeded.SecretRef!)).Should().Be(NewSecret);
                break;
            default:
                stored.State.Should().Be(ConnectionState.Retired, "verification never un-retires a connection it read before the retirement");
                break;
        }

        var actions = (await AuditRowsAsync(scratch, seeded.ConnectionId)).Select(r => (string)r["Action"]!).ToList();
        actions.Should().Contain(AuditActionNames.ConnectionVerified).And.HaveCountGreaterThan(1, "the other change recorded its own row");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_record_waits_for_the_connection_row_like_every_other_connection_writer(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: false);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Connections", seeded.ConnectionId);
        var verify = VerifyAsync(scratch, principal, seeded.ConnectionId, Answer(ConnectionVerificationStatus.Success));
        await holder.WaitUntilBlockedAsync(scratch, "Connections", verify);
        (await AuditRowsAsync(scratch, seeded.ConnectionId)).Should().BeEmpty("nothing is recorded while the row is held");

        await holder.CommitAsync();
        await verify;

        (await AuditRowsAsync(scratch, seeded.ConnectionId)).Should().ContainSingle();
    }

    // ---- Verification: M6 ----

    public static IEnumerable<object[]> OutcomeCases() => new[] { true, false }.Select(c => new object[] { c });

    [Fact]
    public async Task An_audit_insert_failure_leaves_no_verification_and_no_row()
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Vfy", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, principal)).AuditFailureLeavesNothingAsync(principal);
    }

    [Fact]
    public async Task A_lost_acknowledgement_is_verified_and_reconciled_without_running_the_attempt_again()
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Vfy", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, principal)).LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
    }

    [Fact]
    public async Task A_confirmed_rollback_reruns_the_record_once()
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Vfy", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, principal)).ConfirmedRollbackRerunsOnceAsync(principal);
    }

    [Theory]
    [MemberData(nameof(OutcomeCases))]
    public async Task An_unknown_outcome_is_wholly_committed_or_wholly_absent(bool committed)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Vfy", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, principal)).UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed);
    }

    [Fact]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_changes_nothing()
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Vfy", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, principal)).OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }

    private async Task<AuditedOperationM6Runner> RunnerAsync(AuditSqlScratch scratch, IPrincipalContext principal)
    {
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: false);
        return new AuditedOperationM6Runner(
            scratch,
            () => SnapshotAsync(scratch),
            AuditActionNames.ConnectionVerified,
            (db, unit) => Service(db, principal, unit, Answer(ConnectionVerificationStatus.Success)).VerifyConnectionAsync(new ConnectionId(seeded.ConnectionId)).AsTask(),
            "token=abc123");
    }

    // ---- Structure browse: W2 ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_browse_commits_its_required_row_before_the_result_is_delivered_and_records_counts_only(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: false);

        var result = await Browse(scratch, principal).ListSqlSchemasAsync(new ConnectionId(seeded.ConnectionId));

        result!.Items.Should().HaveCount(2);
        var row = (await AuditRowsAsync(scratch, seeded.ConnectionId)).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.ConnectionStructureBrowsed);
        row["Actor"].Should().Be(admin);
        row["Basis"].Should().Be(AuditBases.SecurityEvent);
        row["Category"].Should().Be(AuditCategories.Configuration);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Succeeded);
        row["ResourceContextId"].Should().Be(ResourceContextIds.SharedValue);
        Detail(row, "operation").Should().Be("schemas");
        Detail(row, "tool").Should().Be("sql-structure");
        Detail(row, "result").Should().Be("succeeded");
        Detail(row, "resultCount").Should().Be("2");
        ((string)row["Details"]!).Should().NotContain("hr_secret_schema").And.NotContain("dbo");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_remote_refusal_is_a_delivered_answer_recorded_as_failed_with_its_outcome(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: false);
        var browser = new CannedBrowser { Schemas = () => new SqlStructureResult<SqlSchemaInfo>(ConnectionToolOutcome.RemoteDenied, [], false, 500, "The database refused this request") };

        var result = await Browse(scratch, principal, browser).ListSqlSchemasAsync(new ConnectionId(seeded.ConnectionId));

        result!.Outcome.Should().Be(ConnectionToolOutcome.RemoteDenied);
        var row = (await AuditRowsAsync(scratch, seeded.ConnectionId)).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.ConnectionStructureBrowsed);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Failed);
        Detail(row, "result").Should().Be("remote_denied");
        ((string)row["Details"]!).Should().NotContain("refused");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_browse_whose_required_row_cannot_be_written_delivers_no_result_and_leaves_no_row(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: false);
        var fail = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        var browser = new CannedBrowser();

        var act = () => Browse(scratch, principal, browser, fail).ListSqlSchemasAsync(new ConnectionId(seeded.ConnectionId)).AsTask();

        await act.Should().ThrowAsync<Exception>("no audit, no data");
        fail.Failures.Should().BeGreaterThan(0);
        browser.Calls.Should().Be(1, "the remote request was made; only the delivery is withheld");
        (await AuditRowsAsync(scratch, seeded.ConnectionId)).Should().BeEmpty();
    }

    // ---- Structure browse: a request that threw is a best-effort attempt row ----

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_browse_that_threw_or_was_cancelled_leaves_a_failed_attempt_row_and_the_original_exception(bool cancelled, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: false);
        Exception thrown = cancelled ? new OperationCanceledException() : new TimeoutException(RemoteText);
        var browser = new CannedBrowser { Throw = thrown };

        var act = () => Browse(scratch, principal, browser).ListSqlSchemasAsync(new ConnectionId(seeded.ConnectionId)).AsTask();

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(thrown);
        var row = (await AuditRowsAsync(scratch, seeded.ConnectionId)).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.ConnectionStructureBrowseFailed);
        row["Actor"].Should().Be(admin);
        row["Basis"].Should().Be(AuditBases.ExternalAttempt);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Failed);
        Detail(row, "reason").Should().Be(cancelled ? "cancelled" : "failed");
        ((string)row["Details"]!).Should().NotContain("token=abc123").And.NotContain("sentinel-remote-host");
    }

    [Fact]
    public async Task A_broken_attempt_writer_does_not_replace_the_original_exception()
    {
        var scratch = _catalogs.Locking;
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: false);
        var fail = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        var thrown = new TimeoutException(RemoteText);

        var act = () => Browse(scratch, principal, new CannedBrowser { Throw = thrown }, fail).ListSqlSchemasAsync(new ConnectionId(seeded.ConnectionId)).AsTask();

        (await act.Should().ThrowAsync<TimeoutException>()).Which.Should().BeSameAs(thrown);
        fail.Failures.Should().BeGreaterThan(0);
        (await AuditRowsAsync(scratch, seeded.ConnectionId)).Should().BeEmpty();
    }

    // ---- wiring ----

    private static ConnectionVerificationResult Ok(ConnectionVerificationStatus status) => new(status, DateTimeOffset.UtcNow, RemoteText);

    private static ScriptedVerification Answer(ConnectionVerificationStatus status) =>
        new((_, _) => Task.FromResult(Ok(status)));

    private static async Task<Connection> VerifyAsync(AuditSqlScratch scratch, IPrincipalContext principal, string connectionId, IConnectionVerificationService verification)
    {
        await using var db = scratch.NewDb();
        return await Service(db, principal, NewUnit(db, principal), verification).VerifyConnectionAsync(new ConnectionId(connectionId));
    }

    private static ConnectionToolAccess Browse(
        AuditSqlScratch scratch, IPrincipalContext principal, CannedBrowser? browser = null, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var db = scratch.NewDb();
        return new ConnectionToolAccess(
            principal,
            new AllowAll(),
            new SqlConnectionRepository(db),
            [browser ?? new CannedBrowser()],
            new SqlAuditEventWriter(scratch.Options(interceptors), principals: principal),
            new ConnectionToolRequestLimiter());
    }

    private sealed class ScriptedVerification(Func<Connection, CancellationToken, Task<ConnectionVerificationResult>> probe) : IConnectionVerificationService
    {
        public async ValueTask<ConnectionVerificationResult> VerifyAsync(Connection connection, CancellationToken cancellationToken = default) =>
            await probe(connection, cancellationToken);
    }

    /// <summary>A browser returning canned structure whose CONTENT is recognisable, so a leak into the audit log is detectable.</summary>
    private sealed class CannedBrowser : ISqlStructureBrowser
    {
        public int Calls { get; private set; }
        public Func<SqlStructureResult<SqlSchemaInfo>>? Schemas { get; init; }
        public Exception? Throw { get; init; }

        public ConnectorDefinitionId ConnectorDefinitionId => Sql;
        public ConnectionToolAvailability Availability => ConnectionToolAvailability.Available;

        public ValueTask<SqlStructureResult<SqlSchemaInfo>> ListSchemasAsync(Connection connection, CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw is not null)
                throw Throw;
            return ValueTask.FromResult(Schemas?.Invoke() ?? new SqlStructureResult<SqlSchemaInfo>(
                ConnectionToolOutcome.Succeeded, [new SqlSchemaInfo("hr_secret_schema"), new SqlSchemaInfo("dbo")], false, 500));
        }

        public ValueTask<SqlStructureResult<SqlTableInfo>> ListTablesAsync(Connection connection, string schema, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<SqlStructureResult<SqlColumnInfo>> ListColumnsAsync(Connection connection, string schema, string table, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
