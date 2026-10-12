using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 4: binding create (integration-owned and contextless capability) and delete against SQL Server (design gate
/// §13.4 to §13.6, M6, M7). The change and its one primary row commit together or not at all; a delete whose acknowledgement was
/// lost is verified from the committed row and reported as the success it was, though the binding is now gone. Fails, never
/// skips, without SQL Server. The races with the integration delete are in <c>IntegrationRowLockSqlServerTests</c>.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class BindingLifecycleAuditSqlServerTests : IAsyncLifetime
{
    private const string ModuleId = "Sample.Mod";
    private const string SlotName = "source";
    private const string LifecycleConnectionId = "conn-lifecycle";

    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public BindingLifecycleAuditSqlServerTests(ITestOutputHelper output) => _output = output;

    public enum Op
    {
        CreateOwned,
        CreateCapability,
        DeleteOwned,
        DeleteCapability,
    }

    public static IEnumerable<object[]> Ops() => Enum.GetValues<Op>().Select(op => new object[] { op });

    public static IEnumerable<object[]> OpsWithOutcome() =>
        from op in Enum.GetValues<Op>()
        from committed in new[] { true, false }
        select new object[] { op, committed };

    public async Task InitializeAsync()
    {
        _db = await AuditSqlScratch.CreateAsync("Apperture_Aud14Binding_");
        await _db.ExecuteAsync(
            $"""
            INSERT INTO Connections (Id, ConnectorDefinitionId, Name, ConfigJson, SecretRefsJson, ResourceContextId, State, LastVerificationStatus)
            VALUES (N'{LifecycleConnectionId}', N'test.connector', N'Lifecycle Connection', N'{EmptyJson}', N'{EmptyJson}', N'shared', 0, 0);
            """);
    }

    private const string EmptyJson = "{}";

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
        var arranged = await ArrangeAsync(op, principal);
        var before = await SnapshotAsync();

        await using (var db = _db.NewDb())
            await arranged.Run(db, NewUnit(db, principal));

        (await SnapshotAsync()).Should().NotBe(before);
        var row = (await AuditRowsAsync()).Should().ContainSingle().Subject;
        row["Action"].Should().Be(arranged.Action);
        row["Actor"].Should().Be(admin);
        row["Category"].Should().Be(AuditCategories.Configuration);
        row["Severity"].Should().Be(AuditSeverities.Standard);
        row["Basis"].Should().Be(AuditBases.Change);
        row["TargetType"].Should().Be(AuditTargetTypes.Binding);
        row["TargetId"].Should().Be(arranged.BindingId ?? await _db.ScalarAsync<string>("SELECT TOP 1 Id FROM Bindings;"));
        row["ModuleId"].Should().Be(ModuleId);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Succeeded);
        row["OperationEventId"].Should().Be(row["EventId"], "it is the primary row of its own operation");
        Detail(row, "slotName").Should().Be(SlotName);
        Detail(row, "moduleId").Should().Be(ModuleId);
        if (op is Op.CreateOwned or Op.DeleteOwned)
        {
            Detail(row, "integrationId").Should().Be(arranged.IntegrationId);
            Detail(row, "connectionId").Should().Be(LifecycleConnectionId);
            row["ResourceContextId"].Should().Be(ResourceContextIds.SharedValue, "the integration's owning context");
        }
        else
        {
            Detail(row, "capabilityId").Should().Be("capability-1");
            Detail(row, "integrationId").Should().BeNull();
            row["ResourceContextId"].Should().BeNull("a contextless binding has no owning context");
        }
    }

    [Fact]
    public async Task Two_creates_for_one_slot_that_both_wait_for_the_integration_row_are_serialized_and_the_second_is_refused()
    {
        var (_, principal) = await AdminAsync();
        var integrationId = await SeedIntegrationAsync();

        async Task<Exception?> Attempt()
        {
            await using var db = _db.NewDb();
            try
            {
                await Service(db, principal, NewUnit(db, principal)).CreateBindingAsync(
                    ModuleId, SlotName, "Contract.Type", new ConnectionId(LifecycleConnectionId), new IntegrationId(integrationId));
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        // Both creates are started while another session holds the integration row. The first takes the ownership gate and is
        // shown by the server to be waiting on the row; the second queues behind it on the gate.
        await using var holder = await LockedRowHolder.TakeAsync(_db.ConnectionString, "Integrations", integrationId);
        var first = Task.Run(Attempt);
        var second = Task.Run(Attempt);
        await holder.WaitUntilBlockedAsync(_db, "Integrations", Task.WhenAny(first, second));
        await holder.CommitAsync();
        var results = await Task.WhenAll(first, second);

        results.Count(r => r is null).Should().Be(1, "there is no unique index on a slot: the integration row lock serializes the two");
        results.Single(r => r is not null).Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("already bound");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Bindings;")).Should().Be(1);
        (await AuditRowsAsync()).Should().ContainSingle(r => (string)r["Action"] == AuditActionNames.BindingCreated);
    }

    [Fact]
    public async Task A_delete_that_waits_for_the_binding_row_finds_it_gone_and_is_a_quiet_no_op_that_records_nothing()
    {
        var (_, principal) = await AdminAsync();
        var integrationId = await SeedIntegrationAsync();
        var bindingId = await SeedBindingAsync(integrationId);

        // Another session has decided to remove the binding and holds its row. The delete passes its early read, takes the
        // integration row, and is shown by the server to be waiting for the binding row.
        await using var holder = await LockedRowHolder.TakeAsync(_db.ConnectionString, "Bindings", bindingId);
        await using var db = _db.NewDb();
        var delete = Task.Run(() => Service(db, principal, NewUnit(db, principal)).DeleteBindingAsync(new BindingId(bindingId)).AsTask());
        await holder.WaitUntilBlockedAsync(_db, "Bindings", delete);

        await holder.ExecuteAsync("DELETE FROM Bindings WHERE Id = @id;", ("@id", bindingId));
        await holder.CommitAsync();

        await delete;
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Bindings;")).Should().Be(0);
        (await AuditRowsAsync()).Should().BeEmpty("the delete under the lock found nothing to delete, so it declared a no-op");
    }

    [Fact]
    public async Task Two_concurrent_deletes_of_one_binding_remove_it_once_with_one_row_and_neither_fails()
    {
        var (_, principal) = await AdminAsync();
        var integrationId = await SeedIntegrationAsync();
        var bindingId = await SeedBindingAsync(integrationId);

        async Task Delete()
        {
            await using var db = _db.NewDb();
            await Service(db, principal, NewUnit(db, principal)).DeleteBindingAsync(new BindingId(bindingId));
        }

        await Task.WhenAll(Delete(), Delete());

        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM Bindings;")).Should().Be(0);
        (await AuditRowsAsync()).Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.BindingDeleted, "exactly one primary row");
    }

    [Fact]
    public async Task Deleting_a_missing_binding_is_a_quiet_no_op_that_records_nothing()
    {
        var (_, principal) = await AdminAsync();

        await using var db = _db.NewDb();
        await Service(db, principal, NewUnit(db, principal)).DeleteBindingAsync(new BindingId("bnd-missing"));

        (await AuditRowsAsync()).Should().BeEmpty();
    }

    // ---- M6 ----

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task An_audit_insert_failure_leaves_no_partial_change(Op op)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op, principal)).AuditFailureLeavesNothingAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_lost_acknowledgement_is_verified_and_reconciled_without_running_the_attempt_again(Op op)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op, principal)).LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_confirmed_rollback_reruns_the_operation_once_with_one_row(Op op)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op, principal)).ConfirmedRollbackRerunsOnceAsync(principal);
    }

    [Theory]
    [MemberData(nameof(OpsWithOutcome))]
    public async Task An_unknown_outcome_is_wholly_committed_or_wholly_absent(Op op, bool committed)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op, principal)).UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed);
    }

    [Theory]
    [InlineData(Op.CreateOwned)]
    [InlineData(Op.DeleteOwned)]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_changes_nothing(Op op)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op, principal)).OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }

    // ---- wiring ----

    private BindingService Service(AppertureDbContext db, IPrincipalContext principal, IAuditedUnitOfWork unit) =>
        new(
            principal,
            new AllowAllResource(),
            new SqlBindingRepository(db),
            new SqlConnectionRepository(db),
            new EveryConnectionEligible(db),
            new SqlIntegrationRepository(db),
            db,
            new SqlResourceOwnershipMutationGate(db),
            unit);

    private sealed record Arranged(
        string Action,
        string? IntegrationId,
        string? BindingId,
        Func<AppertureDbContext, IAuditedUnitOfWork, Task> Run);

    private async Task<Arranged> ArrangeAsync(Op op, IPrincipalContext principal)
    {
        switch (op)
        {
            case Op.CreateOwned:
            {
                var integrationId = await SeedIntegrationAsync();
                return new Arranged(
                    AuditActionNames.BindingCreated,
                    integrationId,
                    null,
                    (db, unit) => Service(db, principal, unit)
                        .CreateBindingAsync(ModuleId, SlotName, "Contract.Type", new ConnectionId(LifecycleConnectionId), new IntegrationId(integrationId)).AsTask());
            }

            case Op.CreateCapability:
                return new Arranged(
                    AuditActionNames.BindingCreated,
                    null,
                    null,
                    (db, unit) => Service(db, principal, unit).CreateCapabilityBindingAsync(ModuleId, SlotName, "Contract.Type", "capability-1").AsTask());
            case Op.DeleteOwned:
            {
                var integrationId = await SeedIntegrationAsync();
                var bindingId = await SeedBindingAsync(integrationId);
                return new Arranged(
                    AuditActionNames.BindingDeleted,
                    integrationId,
                    bindingId,
                    (db, unit) => Service(db, principal, unit).DeleteBindingAsync(new BindingId(bindingId)).AsTask());
            }

            default:
            {
                var bindingId = await SeedBindingAsync(integrationId: null, capabilityId: "capability-1");
                return new Arranged(
                    AuditActionNames.BindingDeleted,
                    null,
                    bindingId,
                    (db, unit) => Service(db, principal, unit).DeleteBindingAsync(new BindingId(bindingId)).AsTask());
            }
        }
    }

    private async Task<AuditedOperationM6Runner> RunnerAsync(Op op, IPrincipalContext principal)
    {
        var arranged = await ArrangeAsync(op, principal);
        return new AuditedOperationM6Runner(_db, SnapshotAsync, arranged.Action, arranged.Run);
    }

    private async Task<string> SnapshotAsync()
    {
        var rows = await _db.RowsAsync("SELECT Id, IntegrationId, ModuleId, SlotName, ConnectionId, CapabilityId, State FROM Bindings ORDER BY Id;");
        return string.Join("|", rows.Select(r => string.Join(",", r.Values.Select(v => v?.ToString()))));
    }

    private async Task<string> SeedIntegrationAsync()
    {
        var id = "int-bd-" + Guid.NewGuid().ToString("N")[..10];
        await _db.ExecuteAsync(
            $"""
            INSERT INTO Integrations (Id, DisplayName, ModuleId, ScopeKey, ResourceContextId, State, CreatedAt, UpdatedAt)
            VALUES (N'{id}', N'Binding Test Integration', N'{ModuleId}', N'instance', N'shared', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);
        return id;
    }

    private async Task<string> SeedBindingAsync(string? integrationId, string? capabilityId = null)
    {
        var id = "bnd-" + Guid.NewGuid().ToString("N")[..10];
        var integration = integrationId is null ? "NULL" : $"N'{integrationId}'";
        var connection = capabilityId is null ? $"N'{LifecycleConnectionId}'" : "NULL";
        var capability = capabilityId is null ? "NULL" : $"N'{capabilityId}'";
        await _db.ExecuteAsync(
            $"""
            INSERT INTO Bindings (Id, IntegrationId, ModuleId, SlotName, ContractType, ConnectionId, CapabilityId, State)
            VALUES (N'{id}', {integration}, N'{ModuleId}', N'{SlotName}', N'Contract.Type', {connection}, {capability}, 0);
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

    // ---- doubles ----

    private sealed class AllowAllResource : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    /// <summary>Every stored connection is eligible: these tests are about atomicity, not about connector contracts.</summary>
    private sealed class EveryConnectionEligible(AppertureDbContext db) : IBindingEligibilityService
    {
        public async ValueTask<IReadOnlyList<Connection>> GetEligibleConnectionsAsync(
            string moduleId,
            string slotName,
            string contractType,
            ResourceContextId? owningResourceContextId = null,
            CancellationToken cancellationToken = default)
        {
            var connection = await new SqlConnectionRepository(db).GetByIdAsync(new ConnectionId(LifecycleConnectionId), cancellationToken);
            return connection is null ? [] : [connection];
        }
    }
}
