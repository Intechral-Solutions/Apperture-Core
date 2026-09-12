using Apperture.Core.Application.OperationItems;
using Apperture.Core.Domain.Integration;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-2 core invariant: historical context is resolved once at dispatch and snapshotted, never
/// derived later from current ownership.
/// </summary>
public sealed class Ctx2HistoricalProvenanceTests
{
    private static readonly WorkflowDefinitionId WorkflowId = new("Sample.HelloWorld.HelloWorld");

    [Fact]
    public async Task ManualIntegrationDispatch_SnapshotsTheIntegrationsCurrentContext()
    {
        var db = NewDb();
        SeedIntegration(db, "int-1", "app-a");
        await db.SaveChangesAsync();

        var dispatch = NewWorkflowDispatch(db);
        var result = await dispatch.StartAsync(new StartWorkflowRequest(
            WorkflowId, Parameters: null, IdempotencyKey: "k1", CorrelationId: null, IntegrationId: "int-1"));

        result.Should().NotBeNull();
        var run = await db.OperationRuns.SingleAsync(r => r.Id == result!.RunId.Value);
        var instance = await db.WorkflowInstances.SingleAsync(i => i.Id == result!.WorkflowInstanceId.Value);
        var startRequest = await db.WorkflowStartRequests.SingleAsync(r => r.RunId == result!.RunId.Value);

        run.ResourceContextId.Should().Be("app-a");
        instance.ResourceContextId.Should().Be("app-a");
        startRequest.ResourceContextId.Should().Be("app-a");
    }

    [Fact]
    public async Task ContextlessDispatch_StaysNull_IsNeverDefaultedToShared()
    {
        var db = NewDb();
        var dispatch = NewWorkflowDispatch(db);

        var result = await dispatch.StartAsync(new StartWorkflowRequest(
            WorkflowId, Parameters: null, IdempotencyKey: "k2", CorrelationId: null, IntegrationId: null));

        result.Should().NotBeNull();
        var run = await db.OperationRuns.SingleAsync(r => r.Id == result!.RunId.Value);
        run.ResourceContextId.Should().BeNull("a contextless start has no owning resource, and null must not be read as Shared");
    }

    [Fact]
    public async Task HistoricalSnapshot_IsImmutable_WhenCurrentOwnershipLaterChanges()
    {
        var db = NewDb();
        SeedIntegration(db, "int-2", "app-a");
        await db.SaveChangesAsync();

        var dispatch = NewWorkflowDispatch(db);
        var result = await dispatch.StartAsync(new StartWorkflowRequest(
            WorkflowId, Parameters: null, IdempotencyKey: "k3", CorrelationId: null, IntegrationId: "int-2"));

        // Simulate the Integration moving to a different context after the run was dispatched
        // (CTX-3 movement does not exist yet; this is controlled test setup, per the CTX-2 brief).
        var integration = await db.Integrations.SingleAsync(i => i.Id == "int-2");
        integration.ResourceContextId = "app-b";
        await db.SaveChangesAsync();

        var run = await db.OperationRuns.AsNoTracking().SingleAsync(r => r.Id == result!.RunId.Value);
        run.ResourceContextId.Should().Be("app-a", "historical context must never be re-derived from current ownership");
    }

    [Fact]
    public async Task ScheduleRunClaim_SnapshotsTheScheduleDefinitionsOwnContext()
    {
        var db = NewDb();
        var repo = new SqlScheduleRepository(db);

        var claimed = await repo.TryClaimFireAsync(
            "sched-1", DateTimeOffset.UtcNow, "assign-1", "host-1", resourceContextId: "app-standalone");

        claimed.Should().BeTrue();
        var run = await db.ScheduleRuns.SingleAsync(r => r.ScheduleId == "sched-1");
        run.ResourceContextId.Should().Be("app-standalone");
    }

    [Fact]
    public async Task OperationGroup_DerivesContextFromItsOwningRun_NotFromIntegration()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "run-1",
            WorkflowInstanceId = "wi-1",
            DefinitionId = "def-1",
            Status = "requested",
            CreatedAt = DateTimeOffset.UtcNow,
            IntegrationId = "int-3",
            ResourceContextId = "app-a"
        });
        SeedIntegration(db, "int-3", "app-b"); // deliberately different: proves the group does not look here
        await db.SaveChangesAsync();

        var store = new SqlOperationItemStore(db);
        var group = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-1", "Sample.Mod", "int-3", ProcedureId: null, OperationKind: "sync"));

        group.ResourceContextId.Should().Be("app-a", "OperationGroups derive from the Run snapshot, never from Integration ownership");
    }

    private static WorkflowStartDispatchService NewWorkflowDispatch(AppertureDbContext db) =>
        new(
            new StubDefinitionResolver(new WorkflowDefinition(WorkflowId, "HelloWorld", "Sample.HelloWorld", new[] { "Step1" })),
            new SqlWorkflowPersistence(db),
            db);

    private static void SeedIntegration(AppertureDbContext db, string id, string resourceContextId) =>
        db.Integrations.Add(new IntegrationEntity
        {
            Id = id,
            DisplayName = id,
            ModuleId = "Sample.HelloWorld",
            State = (int)IntegrationState.Active,
            ResourceContextId = resourceContextId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

    private static AppertureDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppertureDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class StubDefinitionResolver : IWorkflowDefinitionResolver
    {
        private readonly WorkflowDefinition _definition;
        public StubDefinitionResolver(WorkflowDefinition definition) => _definition = definition;

        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowDefinition?>(
                string.Equals(id.Value, _definition.Id.Value, StringComparison.Ordinal) ? _definition : null);
    }
}
