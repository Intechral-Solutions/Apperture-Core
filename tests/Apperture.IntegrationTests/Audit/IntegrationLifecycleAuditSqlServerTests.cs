using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 4: integration create, update, activate, deactivate and retire against SQL Server (design gate §13.4 to §13.5,
/// M6, M7). Each is one audited unit: the change and its one primary row commit together or not at all, and every way a commit
/// can be uncertain is verified from the committed primary row. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class IntegrationLifecycleAuditSqlServerTests : IAsyncLifetime
{
    private const string Sentinel = "sentinel-lifecycle-secret-value";
    private const string OtherContext = "app-lifecycle-b";

    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public IntegrationLifecycleAuditSqlServerTests(ITestOutputHelper output) => _output = output;

    public enum Op
    {
        Create,
        Rename,
        ChangeSettings,
        Deactivate,
        Activate,
        Retire,
        Reactivate,
    }

    public static IEnumerable<object[]> Ops() => Enum.GetValues<Op>().Select(op => new object[] { op });

    public static IEnumerable<object[]> OpsWithOutcome() =>
        from op in Enum.GetValues<Op>()
        from committed in new[] { true, false }
        select new object[] { op, committed };

    public async Task InitializeAsync()
    {
        _db = await AuditSqlScratch.CreateAsync("Apperture_Aud14Lifecycle_");
        await _db.ExecuteAsync(
            $"""
            INSERT INTO ResourceContexts (Id, Name, Slug, Kind, State, IsReserved, CreatedAtUtc, UpdatedAtUtc)
            VALUES (N'{OtherContext}', N'Lifecycle B', N'{OtherContext}', N'{ResourceContextKinds.Application}', N'{ResourceContextStates.Active}', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);
    }

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    // ---- M7: the committed operation ----

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_committed_operation_records_exactly_one_primary_row_attributed_to_the_administrator(Op op)
    {
        var (admin, principal) = await AdminAsync();
        var arranged = await ArrangeAsync(op);
        var before = await SnapshotAsync();

        await using (var db = _db.NewDb())
            await arranged.Run(db, NewUnit(db, principal), principal);

        (await SnapshotAsync()).Should().NotBe(before);
        var row = (await AuditRowsAsync()).Should().ContainSingle().Subject;
        row["Action"].Should().Be(arranged.Action);
        row["Actor"].Should().Be(admin);
        row["Category"].Should().Be(AuditCategories.Configuration);
        row["Severity"].Should().Be(AuditSeverities.Standard);
        row["Basis"].Should().Be(AuditBases.Change);
        row["Visibility"].Should().Be("default");
        row["TargetType"].Should().Be(AuditTargetTypes.Integration);
        row["TargetId"].Should().Be(await IntegrationIdAsync(op, arranged));
        row["ResourceContextId"].Should().Be(ResourceContextIds.SharedValue);
        row["ModuleId"].Should().Be("Sample.Mod");
        row["Outcome"].Should().Be(AuditOutcomeCodes.Succeeded);
        row["OutcomeCode"].Should().Be(AuditOutcomeCodes.Succeeded);
        row["OperationEventId"].Should().Be(row["EventId"], "it is the primary row of its own operation");
        Detail(row, "name").Should().NotBeNullOrEmpty();
        arranged.AssertDetails(row);
        ((string)row["Details"]!).Should().NotContain(Sentinel, "no settings key or value is recorded");
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Details LIKE N'%{Sentinel}%';")).Should().Be(0);
    }

    [Fact]
    public async Task A_request_that_changes_nothing_writes_nothing_and_records_nothing()
    {
        var (_, principal) = await AdminAsync();
        var id = await SeedIntegrationAsync(IntegrationState.Active, settingsJson: "{\"a\":\"1\"}");
        var before = await _db.RowsAsync($"SELECT * FROM Integrations WHERE Id = N'{id}';");

        await using (var db = _db.NewDb())
        {
            var service = Service(db, principal, NewUnit(db, principal));
            await service.ActivateIntegrationAsync(new IntegrationId(id));
            await service.UpdateIntegrationAsync(new IntegrationId(id), new UpdateIntegrationRequest(DisplayName: "Lifecycle Integration"));
            await service.UpdateIntegrationAsync(new IntegrationId(id), new UpdateIntegrationRequest(Settings: new Dictionary<string, string> { ["a"] = "1" }));
        }

        (await _db.RowsAsync($"SELECT * FROM Integrations WHERE Id = N'{id}';")).Should().BeEquivalentTo(before, "not even UpdatedAt moved");
        (await AuditRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_missing_integration_is_a_typed_not_found_that_records_nothing()
    {
        var (_, principal) = await AdminAsync();

        await using var db = _db.NewDb();
        var act = () => Service(db, principal, NewUnit(db, principal)).RetireIntegrationAsync(new IntegrationId("int-missing")).AsTask();

        await act.Should().ThrowAsync<IntegrationNotFoundException>();
        (await AuditRowsAsync()).Should().BeEmpty();
    }

    // ---- M6 ----

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task An_audit_insert_failure_leaves_no_partial_change(Op op)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op)).AuditFailureLeavesNothingAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_lost_acknowledgement_is_verified_and_reconciled_without_running_the_attempt_again(Op op)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op)).LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_confirmed_rollback_reruns_the_operation_once_with_one_row(Op op)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op)).ConfirmedRollbackRerunsOnceAsync(principal);
    }

    [Theory]
    [MemberData(nameof(OpsWithOutcome))]
    public async Task An_unknown_outcome_is_wholly_committed_or_wholly_absent(Op op, bool committed)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op)).UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed);
    }

    [Theory]
    [InlineData(Op.Create)]
    [InlineData(Op.Retire)]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_changes_nothing(Op op)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op)).OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }

    // ---- Authorization against the integration as locked, and concurrent updates ----

    [Fact]
    public async Task An_ownership_move_that_commits_while_the_update_waits_is_what_the_authorization_under_the_lock_reads()
    {
        var (_, principal) = await AdminAsync();
        var id = await SeedIntegrationAsync(IntegrationState.Active);
        var authorization = new ContextAwareUpdateAuthorization(_db, deniedContext: OtherContext);

        // The mover's order: the integration row (after the gate), held while the update waits for it.
        await using var mover = await RowHolder.TakeAsync(_db.ConnectionString, id);
        await using var db = _db.NewDb();
        var update = Task.Run(() => Service(db, principal, NewUnit(db, principal), authorization)
            .UpdateIntegrationAsync(new IntegrationId(id), new UpdateIntegrationRequest(DisplayName: "Renamed")).AsTask());
        await WaitUntilBlockedOnIntegrationsAsync(mover.SessionId, update);
        await mover.ExecuteAsync("UPDATE [Integrations] SET [ResourceContextId] = @to WHERE [Id] = @id;", ("@to", OtherContext), ("@id", id));
        await mover.CommitAsync();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => update);
        thrown.Should().BeOfType<UnauthorizedAccessException>("the caller may update in Shared but not in the context the integration moved to");
        authorization.ContextsSeen.Should().Equal("shared", OtherContext);
        (await _db.ScalarAsync<string>($"SELECT DisplayName FROM Integrations WHERE Id = N'{id}';")).Should().Be("Lifecycle Integration");
        (await AuditRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_updates_of_different_fields_are_serialized_and_both_survive()
    {
        var (_, principal) = await AdminAsync();
        var id = await SeedIntegrationAsync(IntegrationState.Active);

        async Task Update(UpdateIntegrationRequest request)
        {
            await using var db = _db.NewDb();
            await Service(db, principal, NewUnit(db, principal)).UpdateIntegrationAsync(new IntegrationId(id), request);
        }

        await Task.WhenAll(
            Update(new UpdateIntegrationRequest(DisplayName: "Renamed")),
            Update(new UpdateIntegrationRequest(Settings: new Dictionary<string, string> { ["a"] = "1" })));

        var row = (await _db.RowsAsync($"SELECT DisplayName, SettingsJson FROM Integrations WHERE Id = N'{id}';")).Single();
        row["DisplayName"].Should().Be("Renamed", "the other update merged over the locked row, not over its stale snapshot");
        ((string)row["SettingsJson"]!).Should().Contain("\"a\"");
        (await AuditRowsAsync()).Should().HaveCount(2).And.OnlyContain(r => (string)r["Action"]! == AuditActionNames.IntegrationUpdated);
    }

    // ---- wiring ----

    private IntegrationManagementService Service(
        AppertureDbContext db, IPrincipalContext principal, IAuditedUnitOfWork unit, IAuthorizationEvaluationService? authorization = null) =>
        new(
            principal,
            authorization ?? new AllowAll(),
            new SqlIntegrationRepository(db),
            new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
            db,
            new SqlResourceOwnershipMutationGate(db),
            auditedUnitOfWork: unit);

    private sealed record Arranged(
        string Action,
        string? IntegrationId,
        Func<AppertureDbContext, IAuditedUnitOfWork, IPrincipalContext, Task> Run,
        Action<Dictionary<string, object?>> AssertDetails);

    private async Task<Arranged> ArrangeAsync(Op op)
    {
        string? id = null;
        switch (op)
        {
            case Op.Create:
                return new Arranged(
                    AuditActionNames.IntegrationCreated,
                    null,
                    (db, unit, principal) => Service(db, principal, unit).CreateIntegrationAsync(
                        new CreateIntegrationRequest("Lifecycle Integration", "Sample.Mod", null, new Dictionary<string, string> { ["endpoint"] = Sentinel }, null)).AsTask(),
                    row =>
                    {
                        Detail(row, "settingsProvided").Should().Be("true");
                        Detail(row, "moduleId").Should().Be("Sample.Mod");
                    });
            case Op.Rename:
                id = await SeedIntegrationAsync(IntegrationState.Active);
                return Update(id, AuditActionNames.IntegrationUpdated, new UpdateIntegrationRequest(DisplayName: "Renamed"), row =>
                {
                    Detail(row, "name").Should().Be("Renamed");
                    Changed(row).Should().Equal("displayName");
                });
            case Op.ChangeSettings:
                id = await SeedIntegrationAsync(IntegrationState.Active);
                return Update(
                    id,
                    AuditActionNames.IntegrationUpdated,
                    new UpdateIntegrationRequest(Settings: new Dictionary<string, string> { ["endpoint"] = Sentinel }),
                    row => Changed(row).Should().Equal("settings"));
            case Op.Deactivate:
                id = await SeedIntegrationAsync(IntegrationState.Active);
                return Transition(id, AuditActionNames.IntegrationDeactivated, IntegrationState.Inactive, "active", "inactive");
            case Op.Activate:
                id = await SeedIntegrationAsync(IntegrationState.Inactive);
                return Transition(id, AuditActionNames.IntegrationActivated, IntegrationState.Active, "inactive", "active");
            case Op.Retire:
                id = await SeedIntegrationAsync(IntegrationState.Active);
                return Transition(id, AuditActionNames.IntegrationRetired, IntegrationState.Retired, "active", "retired");
            default:
                id = await SeedIntegrationAsync(IntegrationState.Retired);
                return Transition(id, AuditActionNames.IntegrationActivated, IntegrationState.Active, "retired", "active");
        }

        Arranged Update(string integrationId, string action, UpdateIntegrationRequest request, Action<Dictionary<string, object?>> assert) =>
            new(action, integrationId,
                (db, unit, principal) => Service(db, principal, unit).UpdateIntegrationAsync(new IntegrationId(integrationId), request).AsTask(),
                assert);

        Arranged Transition(string integrationId, string action, IntegrationState to, string from, string toName) =>
            Update(integrationId, action, new UpdateIntegrationRequest(State: to), row =>
            {
                Detail(row, "fromState").Should().Be(from);
                Detail(row, "toState").Should().Be(toName);
            });
    }

    private async Task<AuditedOperationM6Runner> RunnerAsync(Op op)
    {
        var arranged = await ArrangeAsync(op);
        var principal = (await AdminAsync()).Principal;
        return new AuditedOperationM6Runner(
            _db,
            SnapshotAsync,
            arranged.Action,
            (db, unit) => arranged.Run(db, unit, principal),
            Sentinel);
    }

    private async Task<string> IntegrationIdAsync(Op op, Arranged arranged) =>
        arranged.IntegrationId
        ?? await _db.ScalarAsync<string>("SELECT TOP 1 Id FROM Integrations ORDER BY CreatedAt DESC;");

    private async Task<string> SnapshotAsync()
    {
        var rows = await _db.RowsAsync("SELECT Id, DisplayName, ModuleId, State, SettingsJson, ResourceContextId FROM Integrations ORDER BY Id;");
        return string.Join("|", rows.Select(r => string.Join(",", r.Values.Select(v => v?.ToString()))));
    }

    private async Task<string> SeedIntegrationAsync(IntegrationState state, string? settingsJson = null)
    {
        var id = "int-lc-" + Guid.NewGuid().ToString("N")[..10];
        var settings = settingsJson is null ? "NULL" : $"N'{settingsJson}'";
        await _db.ExecuteAsync(
            $"""
            INSERT INTO Integrations (Id, DisplayName, ModuleId, ScopeKey, ResourceContextId, State, SettingsJson, CreatedAt, UpdatedAt)
            VALUES (N'{id}', N'Lifecycle Integration', N'Sample.Mod', N'instance', N'shared', {(int)state}, {settings}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);
        return id;
    }

    private async Task<(string Admin, Principal Principal)> AdminAsync()
    {
        var id = NewUserId();
        await _db.AddUserAsync(id, NewUsername("admin"));
        return (id, new Principal(id));
    }

    private Task<List<Dictionary<string, object?>>> AuditRowsAsync() => _db.RowsAsync("SELECT * FROM AuditLogs ORDER BY Id;");

    private static string? Detail(Dictionary<string, object?> row, string name)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        if (!details.RootElement.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    private static IReadOnlyList<string> Changed(Dictionary<string, object?> row)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        return details.RootElement.TryGetProperty("changed", out var changed)
            ? changed.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [];
    }

    /// <summary>Waits until the server itself lists a request, blocked by the holder, waiting for a lock on an Integrations row.</summary>
    private async Task WaitUntilBlockedOnIntegrationsAsync(int holderSession, Task writer)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (writer.IsCompleted)
                throw new Xunit.Sdk.XunitException("The writer did not wait for the integration row.");
            var blocked = await _db.ScalarAsync<int>(
                $"""
                SELECT COUNT(*)
                FROM sys.dm_tran_locks AS l
                INNER JOIN sys.dm_exec_requests AS r ON r.session_id = l.request_session_id
                INNER JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
                WHERE l.request_status = N'WAIT' AND l.resource_database_id = DB_ID() AND l.resource_type = N'KEY'
                  AND OBJECT_NAME(p.object_id) = N'Integrations' AND r.blocking_session_id = {holderSession};
                """);
            if (blocked > 0)
                return;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("No request was seen waiting on the Integrations row within 30 seconds.");
    }

    // ---- doubles ----

    private sealed class AllowAll : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    /// <summary>Allows <c>integration.update</c> unless the integration is owned by the denied context, read at each check.</summary>
    private sealed class ContextAwareUpdateAuthorization(AuditSqlScratch scratch, string deniedContext) : IAuthorizationEvaluationService
    {
        public List<string> ContextsSeen { get; } = [];

        public async ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(context.Permission.Action, ActionConstants.IntegrationUpdate, StringComparison.Ordinal))
                return true;
            var owner = await scratch.ScalarAsync<string>(
                $"SELECT ResourceContextId FROM Integrations WITH (NOLOCK) WHERE Id = N'{context.ResourceIdentity!.ResourceId}';");
            ContextsSeen.Add(owner);
            return !string.Equals(owner, deniedContext, StringComparison.Ordinal);
        }
    }

    /// <summary>A second session that holds one integration row (<c>UPDLOCK</c>, as every locking writer takes it) in an open transaction.</summary>
    private sealed class RowHolder : IAsyncDisposable
    {
        private readonly Microsoft.Data.SqlClient.SqlConnection _connection;
        private Microsoft.Data.SqlClient.SqlTransaction? _transaction;

        private RowHolder(Microsoft.Data.SqlClient.SqlConnection connection, Microsoft.Data.SqlClient.SqlTransaction transaction, int sessionId)
        {
            _connection = connection;
            _transaction = transaction;
            SessionId = sessionId;
        }

        public int SessionId { get; }

        public static async Task<RowHolder> TakeAsync(string connectionString, string integrationId)
        {
            var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
            await connection.OpenAsync();
            var transaction = (Microsoft.Data.SqlClient.SqlTransaction)await connection.BeginTransactionAsync();
            await using (var lockCommand = connection.CreateCommand())
            {
                lockCommand.Transaction = transaction;
                lockCommand.CommandText = "SELECT [Id] FROM [Integrations] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = @id;";
                lockCommand.Parameters.AddWithValue("@id", integrationId);
                (await lockCommand.ExecuteScalarAsync()).Should().NotBeNull();
            }

            await using var spid = connection.CreateCommand();
            spid.Transaction = transaction;
            spid.CommandText = "SELECT CAST(@@SPID AS int);";
            return new RowHolder(connection, transaction, (int)(await spid.ExecuteScalarAsync())!);
        }

        public async Task ExecuteAsync(string sql, params (string Name, string Value)[] parameters)
        {
            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task CommitAsync()
        {
            await _transaction!.CommitAsync();
            _transaction = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_transaction is not null)
            {
                try
                {
                    await _transaction.RollbackAsync();
                }
                catch
                {
                    // The connection is closed next either way.
                }
            }

            await _connection.DisposeAsync();
        }
    }
}
