using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Runtime;
using Apperture.Core.Domain.Workflow;
using Apperture.Core.Security.Trust;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 7: the runtime shutdown request as an audited unit against SQL Server on locking READ COMMITTED and
/// READ_COMMITTED_SNAPSHOT catalogs. M7: the request row and the one audit row of the request (actor, target, outcome, category,
/// severity, basis, visibility, details), with <c>RequestedBy</c> equal to the actor the row records. A duplicate request, from
/// anyone, returns the original request unchanged and records nothing. Races: requests queued on the runtime identity row in both
/// orders (one request, one audit row, the first caller's actor), and a queued request committed by a writer that bypassed that
/// lock (no audit row; the observable result is the winner's request on both isolation modes, reached through the check on a
/// locking catalog and through the unique index on a snapshot catalog). M6: an audit insert failure leaves nothing, a lost
/// acknowledgement is verified and reconstructs the real request, a confirmed rollback reruns once, an unknown outcome is wholly
/// committed or wholly absent, an operation lock that is not granted changes nothing. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class RuntimeShutdownRequestSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string Environment = "local";

    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public RuntimeShutdownRequestSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    /// <summary>A catalog of this case's own, for the cases that need an empty audit table (the M6 matrix). The audit table is append-only.</summary>
    private static Task<AuditSqlScratch> NewScratchAsync(bool rcsi) => AuditIsolation.CreateAsync("Apperture_Aud14Rt", rcsi);

    public static IEnumerable<object[]> Isolation() => [[false], [true]];

    public static IEnumerable<object[]> Races() =>
        from firstIsA in new[] { true, false }
        from rcsi in new[] { false, true }
        select new object[] { firstIsA, rcsi };

    // ---- the service over a real catalog ----

    private sealed class Env : IEnvironmentContext
    {
        public string EnvironmentId => Environment;
    }

    private static RuntimeControlService Service(AppertureDbContext db, IAuditedUnitOfWork unit, IRuntimeIdentityService? identity = null) =>
        new(db, identity ?? new SqlRuntimeIdentityService(db), new Env(), unit);

    /// <summary>An identity service that always reports an eligible runtime, whatever the row says now.</summary>
    private sealed class StaleIdentityService : IRuntimeIdentityService
    {
        public ValueTask<RuntimeIdentity?> GetByIdAsync(string runtimeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimeIdentity?>(new RuntimeIdentity(runtimeId, "Sample Runtime", "PublicKey", "pk", null, [RuntimeCapabilityCatalog.ControlShutdown], DateTimeOffset.UtcNow));

        public ValueTask<IReadOnlyList<RuntimeIdentity>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RuntimeTrustValidationResult> ValidateAsync(string runtimeId, string presentedCredential, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<EnrollmentResult> RequestCredentialRolloverAsync(RuntimeCredentialRolloverRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static async Task<string> SeedRuntimeAsync(AuditSqlScratch scratch, bool declareShutdown = true)
    {
        var id = "rt-" + Guid.NewGuid().ToString("N")[..12];
        var scopes = declareShutdown ? $"[\"{RuntimeCapabilityCatalog.ControlShutdown}\"]" : "[]";
        await scratch.ExecuteAsync(
            $"""
            INSERT INTO RuntimeIdentities (Id, DisplayName, CredentialType, PublicKeyOrThumbprint, ManagedIdentityPrincipalId, DeclaredScopesJson, RegisteredAt)
            VALUES (N'{id}', N'Sample Runtime', N'PublicKey', N'pk', NULL, N'{scopes}', SYSDATETIMEOFFSET());
            """);
        return id;
    }

    private static string ScopeKey(string runtimeId) => ScopeKeyConstants.RuntimeInstance(Environment, runtimeId);

    private static Task<List<Dictionary<string, object?>>> RequestRowsAsync(AuditSqlScratch scratch, string runtimeId) =>
        scratch.RowsAsync($"SELECT * FROM RuntimeControlRequests WHERE RuntimeId = N'{runtimeId}' ORDER BY Id;");

    private static Task<List<Dictionary<string, object?>>> AuditRowsAsync(AuditSqlScratch scratch, string? runtimeId = null) =>
        scratch.RowsAsync(runtimeId is null
            ? "SELECT * FROM AuditLogs ORDER BY Id;"
            : $"SELECT * FROM AuditLogs WHERE TargetId = N'{ScopeKey(runtimeId)}' ORDER BY Id;");

    /// <summary>Every request row of every runtime, as one comparable text (the M6 snapshot).</summary>
    private static async Task<string> SnapshotAsync(AuditSqlScratch scratch) =>
        string.Join("|", (await scratch.RowsAsync("SELECT * FROM RuntimeControlRequests ORDER BY Id;"))
            .Select(r => string.Join(",", r.Values.Select(v => v?.ToString()))));

    private static string Detail(Dictionary<string, object?> row, string name) =>
        ConnectionSqlHarness.Detail(row, name) ?? string.Empty;

    private static Task<(string Admin, IPrincipalContext Principal)> AdminAsync(AuditSqlScratch scratch) =>
        ConnectionSqlHarness.AdminAsync(scratch);

    /// <summary>
    /// Records the SQL Server error numbers of the saves that failed, to show which path a race took. A save is the level at which
    /// EF reports a unique-key violation (the error surfaces while the insert's result is consumed, after the command is issued).
    /// </summary>
    private sealed class FailedSaveNumbers : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public List<int> Numbers { get; } = [];

        public override Task SaveChangesFailedAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            for (var inner = eventData.Exception; inner is not null; inner = inner.InnerException)
            {
                if (inner is Microsoft.Data.SqlClient.SqlException sql)
                {
                    lock (Numbers)
                        Numbers.Add(sql.Number);
                    break;
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed record Outcome<T>(T? Value, Exception? Error);

    private static async Task<Outcome<T>> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return new Outcome<T>(await Task.Run(operation), null);
        }
        catch (Exception ex)
        {
            return new Outcome<T>(default, ex);
        }
    }

    // ---- M7: the committed request ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_new_request_records_one_queued_row_and_one_audit_row_of_the_same_actor(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var runtime = await SeedRuntimeAsync(scratch);

        RuntimeControlRequestResult? result;
        await using (var db = scratch.NewDb())
            result = await Service(db, NewUnit(db, principal)).RequestShutdownAsync(runtime);

        var requests = await RequestRowsAsync(scratch, runtime);
        var request = requests.Should().ContainSingle().Subject;
        request["Status"].Should().Be(RuntimeControlStatusNames.Queued);
        request["ControlKind"].Should().Be(RuntimeControlKindNames.Shutdown);
        request["RequestedBy"].Should().Be(admin, "the request records the unit's own actor");
        result.Should().NotBeNull();
        result!.Id.Should().Be((long)request["Id"]!);
        result.RequestedBy.Should().Be(admin);

        var rows = await AuditRowsAsync(scratch, runtime);
        var row = rows.Should().ContainSingle("one primary row per completed request").Subject;
        row["Action"].Should().Be(AuditActionNames.RuntimeShutdownRequestCreated);
        row["OperationEventId"].Should().Be(row["EventId"], "the row is the primary row of its own operation");
        row["Actor"].Should().Be(admin);
        row["TargetType"].Should().Be(AuditTargetTypes.RuntimeInstance);
        row["TargetId"].Should().Be(ScopeKey(runtime));
        row["OutcomeCode"].Should().Be(AuditOutcomeCodes.Requested);
        row["Category"].Should().Be(AuditCategories.Operations);
        row["Severity"].Should().Be(AuditSeverities.Standard);
        row["Basis"].Should().Be(AuditBases.Request);
        row["Visibility"].Should().Be("default");
        Detail(row, "runtimeId").Should().Be(runtime);
        Detail(row, "requestId").Should().Be(result.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Detail(row, "controlKind").Should().Be("shutdown");
        using (var details = System.Text.Json.JsonDocument.Parse((string)row["Details"]!))
            details.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("runtimeId", "requestId", "controlKind");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_duplicate_request_from_the_same_or_another_user_returns_the_original_and_records_nothing(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (first, firstPrincipal) = await AdminAsync(scratch);
        var (_, secondPrincipal) = await AdminAsync(scratch);
        var runtime = await SeedRuntimeAsync(scratch);

        RuntimeControlRequestResult? original;
        await using (var db = scratch.NewDb())
            original = await Service(db, NewUnit(db, firstPrincipal)).RequestShutdownAsync(runtime);
        var auditBefore = (await AuditRowsAsync(scratch)).Count;

        foreach (var principal in new[] { firstPrincipal, secondPrincipal })
        {
            RuntimeControlRequestResult? again;
            await using (var db = scratch.NewDb())
                again = await Service(db, NewUnit(db, principal)).RequestShutdownAsync(runtime);

            again.Should().NotBeNull();
            again!.Id.Should().Be(original!.Id);
            again.RequestedBy.Should().Be(first, "an existing request is never re-attributed");
        }

        (await RequestRowsAsync(scratch, runtime)).Should().ContainSingle();
        (await AuditRowsAsync(scratch, runtime)).Should().ContainSingle("a duplicate request records nothing");
        (await AuditRowsAsync(scratch)).Should().HaveCount(auditBefore);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_request_after_the_queued_one_was_applied_is_a_new_request_with_its_own_row(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var runtime = await SeedRuntimeAsync(scratch);
        RuntimeControlRequestResult? first;
        await using (var db = scratch.NewDb())
            first = await Service(db, NewUnit(db, principal)).RequestShutdownAsync(runtime);
        await scratch.ExecuteAsync($"UPDATE RuntimeControlRequests SET Status = N'applied', AppliedAt = SYSDATETIMEOFFSET(), AppliedBy = N'worker-1' WHERE RuntimeId = N'{runtime}';");

        RuntimeControlRequestResult? second;
        await using (var db = scratch.NewDb())
            second = await Service(db, NewUnit(db, principal)).RequestShutdownAsync(runtime);

        second!.Id.Should().NotBe(first!.Id);
        (await RequestRowsAsync(scratch, runtime)).Should().HaveCount(2);
        (await AuditRowsAsync(scratch, runtime)).Should().HaveCount(2);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_unknown_runtime_and_a_runtime_without_the_capability_return_null_and_record_nothing(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var incapable = await SeedRuntimeAsync(scratch, declareShutdown: false);
        var unknown = "rt-missing-" + Guid.NewGuid().ToString("N")[..8];
        var auditBefore = (await AuditRowsAsync(scratch)).Count;

        foreach (var runtime in new[] { incapable, unknown })
        {
            await using var db = scratch.NewDb();
            (await Service(db, NewUnit(db, principal)).RequestShutdownAsync(runtime)).Should().BeNull(runtime);
            (await RequestRowsAsync(scratch, runtime)).Should().BeEmpty(runtime);
            (await AuditRowsAsync(scratch, runtime)).Should().BeEmpty(runtime);
        }

        // The early check says the runtime is eligible, but the row read under the lock is not: a no-op that returns null.
        await using (var db = scratch.NewDb())
        {
            (await Service(db, NewUnit(db, principal), new StaleIdentityService()).RequestShutdownAsync(incapable)).Should().BeNull();
            (await Service(db, NewUnit(db, principal), new StaleIdentityService()).RequestShutdownAsync(unknown)).Should().BeNull();
        }

        foreach (var runtime in new[] { incapable, unknown })
        {
            (await RequestRowsAsync(scratch, runtime)).Should().BeEmpty(runtime);
            (await AuditRowsAsync(scratch, runtime)).Should().BeEmpty(runtime);
        }

        (await AuditRowsAsync(scratch)).Should().HaveCount(auditBefore);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- races ----

    /// <summary>
    /// Two requests for one runtime, started in both orders while a third session holds the runtime identity row. Both wait on the
    /// row, in arrival order. Whichever arrived first queues the request and records the one audit row; the second finds it under
    /// the lock, writes nothing, and reports the same request with the first caller's actor.
    /// </summary>
    [Theory]
    [MemberData(nameof(Races))]
    public async Task Two_requests_queued_on_the_runtime_row_leave_one_request_and_one_audit_row_of_the_first_caller(bool firstIsA, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (adminA, principalA) = await AdminAsync(scratch);
        var (adminB, principalB) = await AdminAsync(scratch);
        var runtime = await SeedRuntimeAsync(scratch);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "RuntimeIdentities", runtime);
        Task<Outcome<RuntimeControlRequestResult?>> Request(IPrincipalContext principal) => RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            return await Service(db, NewUnit(db, principal)).RequestShutdownAsync(runtime);
        });

        var firstPrincipal = firstIsA ? principalA : principalB;
        var secondPrincipal = firstIsA ? principalB : principalA;
        var firstAdmin = firstIsA ? adminA : adminB;
        var first = Request(firstPrincipal);
        await holder.WaitUntilBlockedAsync(scratch, "RuntimeIdentities", first);
        var second = Request(secondPrincipal);
        await holder.WaitUntilBlockedAsync(scratch, "RuntimeIdentities", Task.WhenAny(first, second), minimumWaiters: 2);
        await holder.CommitAsync();
        var one = await first;
        var two = await second;

        one.Error.Should().BeNull();
        two.Error.Should().BeNull();
        one.Value.Should().NotBeNull();
        two.Value.Should().NotBeNull();
        two.Value!.Id.Should().Be(one.Value!.Id, "both callers got the same request");
        one.Value.RequestedBy.Should().Be(firstAdmin);
        two.Value.RequestedBy.Should().Be(firstAdmin, "the second caller is not recorded as the requester");
        var requests = await RequestRowsAsync(scratch, runtime);
        requests.Should().ContainSingle().Which["RequestedBy"].Should().Be(firstAdmin);
        var rows = await AuditRowsAsync(scratch, runtime);
        rows.Should().ContainSingle().Which["Actor"].Should().Be(firstAdmin, "the second request wrote no row");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    /// <summary>
    /// A writer that bypassed the identity lock queues a request and holds it uncommitted. On a locking catalog the queued-request
    /// check itself waits on that row and, once it commits, finds it (the duplicate path). On a snapshot catalog the check does not
    /// see an uncommitted row, so the insert blocks on the unique index and, once the row commits, fails on exactly that index
    /// (the lost-race path). Either way the observable result is the same: the winner's request, no second request row and no
    /// audit row, since nothing of this request committed.
    /// </summary>
    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_request_queued_by_a_writer_that_bypassed_the_lock_is_reported_and_records_nothing(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var runtime = await SeedRuntimeAsync(scratch);
        var auditBefore = (await AuditRowsAsync(scratch)).Count;

        await using var third = await LockedRowHolder.BeginAsync(scratch.ConnectionString);
        await third.ExecuteAsync(
            $"INSERT INTO RuntimeControlRequests (RuntimeId, ControlKind, Status, RequestedAt, RequestedBy) VALUES (N'{runtime}', N'shutdown', N'queued', SYSDATETIMEOFFSET(), N'raw-third-session');");

        var failures = new FailedSaveNumbers();
        var request = RunAsync(async () =>
        {
            await using var db = scratch.NewDb(failures);
            return await Service(db, NewUnit(db, principal)).RequestShutdownAsync(runtime);
        });
        await third.WaitUntilBlockedAsync(scratch, "RuntimeControlRequests", request);
        await third.CommitAsync();
        var outcome = await request;

        outcome.Error.Should().BeNull();
        if (rcsi)
            failures.Numbers.Should().Contain(2601, "the check did not see the uncommitted row, so the insert lost on the unique index");
        else
            failures.Numbers.Should().BeEmpty("the check waited for the row and found it, so no insert was attempted");
        var winner = (await RequestRowsAsync(scratch, runtime)).Should().ContainSingle().Subject;
        winner["RequestedBy"].Should().Be("raw-third-session");
        outcome.Value.Should().NotBeNull();
        outcome.Value!.Id.Should().Be((long)winner["Id"]!);
        outcome.Value.RequestedBy.Should().Be("raw-third-session", "the winner keeps its own attribution");
        (await AuditRowsAsync(scratch, runtime)).Should().BeEmpty("nothing of this request committed");
        (await AuditRowsAsync(scratch)).Should().HaveCount(auditBefore);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- M6 ----

    private async Task<AuditedOperationM6Runner> RunnerAsync(AuditSqlScratch scratch)
    {
        var runtime = await SeedRuntimeAsync(scratch);
        return new AuditedOperationM6Runner(
            scratch,
            () => SnapshotAsync(scratch),
            AuditActionNames.RuntimeShutdownRequestCreated,
            (db, unit) => Service(db, unit).RequestShutdownAsync(runtime).AsTask());
    }

    [Fact]
    public async Task An_audit_insert_failure_leaves_no_request_row()
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch)).AuditFailureLeavesNothingAsync(principal);
    }

    [Fact]
    public async Task A_lost_acknowledgement_is_verified_and_reconciled_without_running_the_attempt_again()
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch)).LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
    }

    [Fact]
    public async Task A_lost_acknowledgement_returns_the_real_request_with_its_original_actor_and_inserts_one_row()
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (admin, principal) = await AdminAsync(scratch);
        var runtime = await SeedRuntimeAsync(scratch);
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        RuntimeControlRequestResult? result;
        await using (var db = scratch.NewDb(lost, attempts))
            result = await Service(db, NewUnit(db, principal)).RequestShutdownAsync(runtime);

        attempts.Started.Should().Be(1, "a verified commit is not run again");
        var request = (await RequestRowsAsync(scratch, runtime)).Should().ContainSingle().Subject;
        result.Should().NotBeNull();
        result!.Id.Should().Be((long)request["Id"]!, "the result is read back by the id the audit row records");
        result.RequestedBy.Should().Be(admin);
        result.Status.Should().Be(RuntimeControlStatusNames.Queued);
        (await AuditRowsAsync(scratch, runtime)).Should().ContainSingle().Which["Actor"].Should().Be(admin);
    }

    [Fact]
    public async Task A_confirmed_rollback_reruns_the_request_once_and_ends_with_one_request_and_one_row()
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch)).ConfirmedRollbackRerunsOnceAsync(principal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unknown_outcome_is_wholly_committed_or_wholly_absent(bool committed)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch)).UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed);
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM RuntimeControlRequests;")).Should().Be(committed ? 1 : 0);
    }

    [Fact]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_changes_nothing()
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch)).OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }
}
