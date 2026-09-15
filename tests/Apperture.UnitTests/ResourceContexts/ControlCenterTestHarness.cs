using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Workflow;
using Apperture.Core.Security.Trust;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3A Plan 6, Task 1: wires the real run-derived Control Center query services
/// (<see cref="ControlActivityQueryService"/>, <see cref="ControlAttentionQueryService"/>) over an
/// in-memory <see cref="AppertureDbContext"/>, following <see cref="InventoryTestHarness"/>'s
/// established pattern (real RunQueryService, same authorization/actor stubs) rather than inventing
/// another harness shape. Runtime identity/control/action-projection dependencies are Moq stubs
/// returning empty results -- Task 1's tests are about run-derived filtering and the stuck-shutdown
/// / rejected-controls DB queries, not runtime identity or action projection.
/// </summary>
public sealed class ControlCenterTestHarness : IAsyncDisposable
{
    private readonly string _databaseName = Guid.NewGuid().ToString("N");
    private readonly InMemoryDatabaseRoot _databaseRoot = new();
    private int _nextSequence;

    public AppertureDbContext Db { get; }
    public TimeProvider Clock { get; }
    public IControlActivityQueryService ActivityService { get; }
    public IControlAttentionQueryService AttentionService { get; }

    public static Task<ControlCenterTestHarness> CreateAsync() => Task.FromResult(new ControlCenterTestHarness());

    private ControlCenterTestHarness()
    {
        Db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(_databaseName, _databaseRoot)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        Clock = new ManualTimeProvider(DateTimeOffset.UtcNow);

        var runQueryService = new RunQueryService(
            Db,
            new StubOperationRegistry(),
            new RunKindResolver(Db),
            new AllowWorkflowReadAuthz(),
            new AllowOperationReadAuthz(),
            new StubWorkflowDefinitionResolver(),
            new StubComposition());

        var environment = new StubEnvironmentContext();
        var actions = new Mock<IControlActionAvailabilityProjection>();
        actions.Setup(x => x.ProjectExecutionRunActionsAsync(
                It.IsAny<RunSummaryDto>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Apperture.Core.Application.ControlFacade.ControlActionAvailabilityDto>());

        var runtimeIdentity = new Mock<IRuntimeIdentityService>();
        runtimeIdentity.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RuntimeIdentity>());

        var runtimeControl = new Mock<IRuntimeControlService>();

        ActivityService = new ControlActivityQueryService(
            runQueryService,
            runtimeIdentity.Object,
            runtimeControl.Object,
            actions.Object,
            new AlwaysAllow(),
            new AlwaysActor(),
            environment,
            Db);

        AttentionService = new ControlAttentionQueryService(
            runQueryService,
            new AlwaysAllow(),
            new AlwaysActor(),
            environment,
            Db,
            new AllowWorkflowReadAuthz());
    }

    public async Task<OperationRunEntity> SeedOperationRunAsync(
        string? resourceContextId,
        DateTimeOffset? createdAt = null,
        string? name = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var sequence = NextSequence();
        var runId = $"run-{sequence}";
        var definitionId = name ?? $"op-def-{sequence}";
        var instanceId = $"op-instance-{sequence}";

        var run = new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = instanceId,
            DefinitionId = definitionId,
            Status = status ?? WorkflowLifecycleStatusNames.Completed,
            CreatedAt = createdAt ?? Clock.GetUtcNow(),
            ResourceContextId = resourceContextId
        };
        Db.OperationRuns.Add(run);
        Db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = runId,
            OperationInstanceId = instanceId,
            OperationDefinitionId = definitionId,
            CreatedAt = createdAt ?? Clock.GetUtcNow()
        });

        await Db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public Task<OperationRunEntity> SeedFailedRunAsync(
        string? resourceContextId,
        DateTimeOffset? createdAt = null,
        string? name = null,
        CancellationToken cancellationToken = default) =>
        SeedOperationRunAsync(resourceContextId, createdAt, name, status: WorkflowLifecycleStatusNames.Failed, cancellationToken);

    public async Task SeedStuckShutdownRequestAsync(
        string runtimeId = "runtime-1",
        DateTimeOffset? requestedAt = null,
        CancellationToken cancellationToken = default)
    {
        Db.RuntimeControlRequests.Add(new RuntimeControlRequestEntity
        {
            RuntimeId = runtimeId,
            ControlKind = RuntimeControlKindNames.Shutdown,
            Status = RuntimeControlStatusNames.Queued,
            // ControlAttentionQueryService.cs's queuedCutoff = UtcNow - 15min filter requires this to be older.
            RequestedAt = requestedAt ?? Clock.GetUtcNow().AddMinutes(-20)
        });
        await Db.SaveChangesAsync(cancellationToken);
    }

    public async Task SeedRejectedExternalControlAsync(
        string runId,
        DateTimeOffset? requestedAt = null,
        CancellationToken cancellationToken = default)
    {
        var sequence = NextSequence();
        Db.WorkflowExternalControlRequests.Add(new WorkflowExternalControlRequestEntity
        {
            RunId = runId,
            WorkflowInstanceId = $"wf-instance-{sequence}",
            WorkflowDefinitionId = $"wf-def-{sequence}",
            ControlKind = WorkflowExternalControlKindNames.Park,
            Status = WorkflowExternalControlStatusNames.Rejected,
            RequestedAt = requestedAt ?? Clock.GetUtcNow()
        });
        await Db.SaveChangesAsync(cancellationToken);
    }

    private int NextSequence() => Interlocked.Increment(ref _nextSequence);

    public async ValueTask DisposeAsync() => await Db.DisposeAsync();

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubEnvironmentContext : IEnvironmentContext
    {
        public string EnvironmentId => "test-env";
    }

    private sealed class AlwaysActor : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new Actor());

        private sealed class Actor : IActor
        {
            public ActorId Id => new("test-actor");
            public ActorKind Kind => ActorKind.Human;
            public string? DisplayName => "Test Actor";
        }
    }

    private sealed class AlwaysAllow : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class StubOperationRegistry : IOperationRegistryService
    {
        public ValueTask<IReadOnlyList<OperationDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(Array.Empty<OperationDefinition>());

        public ValueTask<OperationDefinition?> GetOperationAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<OperationDefinition?>(new OperationDefinition(operationId, operationId.Value, "mod", OperationCategory.Command, null, []));

        public ValueTask<IReadOnlyList<OperationDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>(Array.Empty<OperationDefinition>());

        public ValueTask RegisterAsync(OperationDefinition definition, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class AllowWorkflowReadAuthz : IWorkflowRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class AllowOperationReadAuthz : IOperationRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubWorkflowDefinitionResolver : IWorkflowDefinitionResolver
    {
        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowDefinition?>(null);
    }

    private sealed class StubComposition : Apperture.Core.Application.RuntimeComposition.IRuntimeModuleCompositionQueryService
    {
        public ValueTask<Apperture.Core.Application.RuntimeComposition.HostEffectiveModuleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new Apperture.Core.Application.RuntimeComposition.HostEffectiveModuleSnapshot(
                DateTimeOffset.UtcNow,
                [],
                [],
                [],
                [],
                []));
    }
}
