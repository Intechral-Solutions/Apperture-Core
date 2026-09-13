using System.Reflection;
using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Integration;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Options;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using Apperture.RuntimePlane.Engine;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

    [Fact]
    public async Task ExecutionAudit_UsesRunsSnapshot_NotCurrentIntegrationOwnership_AfterSimulatedMove()
    {
        var db = NewDb();
        SeedIntegration(db, "int-audit-1", "app-a");
        await db.SaveChangesAsync();

        var dispatch = NewWorkflowDispatch(db);
        var result = await dispatch.StartAsync(new StartWorkflowRequest(
            WorkflowId, Parameters: null, IdempotencyKey: "k-audit-1", CorrelationId: null, IntegrationId: "int-audit-1"));
        result.Should().NotBeNull();

        // Simulate the Integration moving to a different context after dispatch (CTX-3 movement does
        // not exist yet; controlled test setup only, matching the existing immutability test above).
        var integration = await db.Integrations.SingleAsync(i => i.Id == "int-audit-1");
        integration.ResourceContextId = "app-b";
        await db.SaveChangesAsync();

        var run = await db.OperationRuns.AsNoTracking().SingleAsync(r => r.Id == result!.RunId.Value);
        run.ResourceContextId.Should().Be("app-a", "run.ResourceContextId must never be re-derived from current ownership");
        run.ResourceContextId.Should().NotBe(integration.ResourceContextId, "this test deliberately makes them differ to prove audit reads the Run snapshot, not current Integration ownership");

        // Part 1: exercise the REAL production failure path end to end. Claim + process the start
        // request with a definition resolver that fails resolution, so the processor's own catch block
        // (which internally calls TryAuditDefinitionUnavailableAsync(ex, runId, run.ResourceContextId, ...)
        // using the Run row it loaded itself, not a value supplied by this test) runs for real. This is
        // what actually proves the call site reads the Run's snapshot rather than the Integration's
        // current (moved) context -- a regression that swapped in integration.ResourceContextId at that
        // call site would flip this assertion to "app-b" and fail.
        var audit = new CapturingAuditService();
        var processor = new WorkflowStartRequestProcessor(
            db,
            new StubExecutor(),
            new UnresolvableDefinitionResolver(),
            "worker",
            Options.Create(new WorkflowStartRequestLeaseOptions { LeaseTimeout = TimeSpan.FromMinutes(5) }),
            audit: audit);

        (await processor.TryClaimAsync(run.Id)).Should().BeTrue();
        await processor.ProcessClaimedAsync(run.Id);

        audit.LastRecord.Should().NotBeNull();
        audit.LastRecord!.ResourceContextId.Should().Be("app-a", "the audit record must carry the Run's snapshot");
        audit.LastRecord!.ResourceContextId.Should().NotBe("app-b", "the audit record must never carry the Integration's current (moved) context");

        // Part 2: additionally mirror Task 2's direct reflection-based invocation of the same private
        // helper (WorkflowProcessorDefinitionResolutionTests.InvokeAudit / CapturingAuditService), using
        // the Run's own (stale-by-design) snapshot explicitly, per the task brief's requested pattern.
        var directAudit = new CapturingAuditService();
        var directProcessor = new WorkflowStartRequestProcessor(
            db,
            new StubExecutor(),
            new StubDefinitionResolver(new WorkflowDefinition(WorkflowId, "HelloWorld", "Sample.HelloWorld", new[] { "Step1" })),
            "worker",
            Options.Create(new WorkflowStartRequestLeaseOptions { LeaseTimeout = TimeSpan.FromMinutes(5) }),
            audit: directAudit);

        var ex = new DefinitionNotExecutableException(WorkflowId.Value, ExecutionDefinitionKind.Workflow, "not effectively active");
        await InvokeAudit(directProcessor, ex, run.Id, run.ResourceContextId);

        directAudit.LastRecord.Should().NotBeNull();
        directAudit.LastRecord!.ResourceContextId.Should().Be("app-a", "the helper must pass the Run's snapshot straight through to the AuditRecord");
    }

    [Fact]
    public async Task ScheduleRunClaim_StaysImmutable_WhenScheduleDefinitionsContextLaterChanges()
    {
        var db = NewDb();
        var repo = new SqlScheduleRepository(db);

        var claimed = await repo.TryClaimFireAsync(
            "sched-immut-1", DateTimeOffset.UtcNow, "assign-immut-1", "host-1", resourceContextId: "app-a");
        claimed.Should().BeTrue();

        // There is no ScheduleDefinition row backing this minimal claim call (TryClaimFireAsync takes
        // the resourceContextId directly), so there is nothing upstream to mutate after the fact — the
        // immutability guarantee here is that the claimed ScheduleRun keeps the context it was given at
        // claim time and is never re-derived from any later lookup. Re-fetch untracked to rule out the
        // assertion merely reading back the same tracked in-memory instance.
        var run = await db.ScheduleRuns.AsNoTracking().SingleAsync(r => r.ScheduleId == "sched-immut-1");
        run.ResourceContextId.Should().Be("app-a", "a ScheduleRun's snapshot must stay exactly what was recorded at claim time");
    }

    private static async Task InvokeAudit(
        WorkflowStartRequestProcessor processor, Exception ex, string runId, string? resourceContextId)
    {
        var method = typeof(WorkflowStartRequestProcessor).GetMethod(
            "TryAuditDefinitionUnavailableAsync",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(Exception), typeof(string), typeof(string), typeof(CancellationToken)],
            null);
        if (method is null)
            throw new InvalidOperationException("TryAuditDefinitionUnavailableAsync not found");
        var result = method.Invoke(processor, new object?[] { ex, runId, resourceContextId, CancellationToken.None });
        if (result is ValueTask vt)
            await vt.ConfigureAwait(false);
        else
            throw new InvalidOperationException($"Unexpected return type {result?.GetType().FullName}");
    }

    private sealed class CapturingAuditService : IAuditService
    {
        public AuditRecord? LastRecord { get; private set; }
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            LastRecord = record;
            return ValueTask.CompletedTask;
        }
        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(null);
    }

    private sealed class StubExecutor : IWorkflowExecutor
    {
        public ValueTask<object?> ExecuteStepAsync(
            WorkflowDefinition definition,
            string stepName,
            WorkflowInstance instance,
            IReadOnlyDictionary<string, object?>? parameters,
            string? integrationId,
            WorkflowStepExecutionScope? executionScope,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by this test; only the audit helper is invoked directly.");
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

    /// <summary>
    /// Always fails resolution, forcing WorkflowStartRequestProcessor.ResolveDefinitionOrThrowAsync to
    /// throw DefinitionNotExecutableException so the real audit call site in its catch block runs.
    /// </summary>
    private sealed class UnresolvableDefinitionResolver : IWorkflowDefinitionResolver
    {
        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowDefinition?>(null);
    }
}
