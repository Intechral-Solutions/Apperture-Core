using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ControlFacade;
using Apperture.Core.Application.RuntimeComposition;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using System;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class RunRouteFamilyParityTests
{
    [Fact]
    public async Task OperationRun_WithoutOperationInstance_IsProjectableAcrossDetailActionsTimeline()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "run-1",
            WorkflowInstanceId = "op-i1",
            DefinitionId = "op-1",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "run-1",
            OperationInstanceId = "op-i1",
            OperationDefinitionId = "op-1",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var kinds = new RunKindResolver(db);
        var detailService = new RunQueryService(
            db,
            new StubOperationRegistry(),
            kinds,
            new AllowWorkflowReadAuthz(),
            new AllowOperationReadAuthz(),
            new StubWorkflowDefinitionResolver(),
            new StubComposition());
        var actionService = new RunActionAvailabilityQueryService(db, kinds, new StubActionProjection(), new AllowWorkflowReadAuthz(), new AllowOperationReadAuthz());
        var timelineService = new RunTimelineQueryService(db, kinds, new AllowWorkflowReadAuthz(), new AllowOperationReadAuthz());

        var detail = await detailService.GetRunAsync(new OperationRunId("run-1"));
        var actions = await actionService.GetRunActionsAsync(new OperationRunId("run-1"));
        var timeline = await timelineService.GetRunTimelineAsync(new OperationRunId("run-1"));

        detail.Should().NotBeNull();
        actions.Should().NotBeNull();
        timeline.Should().NotBeNull();
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
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

    private sealed class StubActionProjection : IControlActionAvailabilityProjection
    {
        public ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectExecutionRunActionsAsync(RunSummaryDto run, bool hasQueuedPark, bool hasQueuedResume, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlActionAvailabilityDto>>(Array.Empty<ControlActionAvailabilityDto>());

        public ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectOperationDefinitionActionsAsync(OperationDefinitionDto operation, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlActionAvailabilityDto>>(Array.Empty<ControlActionAvailabilityDto>());

        public ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectWorkflowDefinitionActionsAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlActionAvailabilityDto>>(Array.Empty<ControlActionAvailabilityDto>());

        public ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectRuntimeInstanceActionsAsync(Apperture.Core.Security.Trust.RuntimeIdentity runtime, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlActionAvailabilityDto>>(Array.Empty<ControlActionAvailabilityDto>());
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

    private sealed class StubComposition : IRuntimeModuleCompositionQueryService
    {
        public ValueTask<HostEffectiveModuleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HostEffectiveModuleSnapshot(
                DateTimeOffset.UtcNow,
                [],
                [],
                [],
                [],
                []));
    }
}
