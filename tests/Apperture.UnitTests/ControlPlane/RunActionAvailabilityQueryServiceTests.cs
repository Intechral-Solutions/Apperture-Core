using System;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ControlFacade;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Workflow;
using Apperture.Core.Security.Trust;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class RunActionAvailabilityQueryServiceTests
{
    [Fact]
    public async Task GetRunActionsAsync_WorkflowRun_ReflectsQueuedParkAndNoScheduleActions()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "run-1",
            WorkflowInstanceId = "wf-i1",
            DefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "run-1",
            WorkflowInstanceId = "wf-i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.WorkflowExternalControlRequests.Add(new WorkflowExternalControlRequestEntity
        {
            RunId = "run-1",
            WorkflowInstanceId = "wf-i1",
            WorkflowDefinitionId = "Sample.HelloWorld.HelloWorld",
            ControlKind = WorkflowExternalControlKindNames.Park,
            Status = WorkflowExternalControlStatusNames.Queued,
            RequestedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new RunActionAvailabilityQueryService(
            db,
            new RunKindResolver(db),
            NewProjection(),
            new AllowWorkflowReadAuthz(),
            new AllowOperationReadAuthz());

        var actions = await service.GetRunActionsAsync(new OperationRunId("run-1"));

        actions.Should().NotBeNull();
        var list = actions!;
        list.Any(x => x.ActionKind == ControlFacade.ActionKindPark && x.AvailabilityState == ControlFacade.ActionAvailabilityAlreadyQueued).Should().BeTrue();
        list.All(x => x.ActionKind.StartsWith("execution.", StringComparison.Ordinal)).Should().BeTrue();
    }

    [Fact]
    public async Task GetRunActionsAsync_OperationRun_ExposesOnlyCancel()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "op-run-1",
            WorkflowInstanceId = "op-i1",
            DefinitionId = "op-1",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "op-run-1",
            OperationInstanceId = "op-i1",
            OperationDefinitionId = "op-1",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new RunActionAvailabilityQueryService(
            db,
            new RunKindResolver(db),
            NewProjection(),
            new AllowWorkflowReadAuthz(),
            new AllowOperationReadAuthz());

        var actions = await service.GetRunActionsAsync(new OperationRunId("op-run-1"));

        actions.Should().NotBeNull();
        var list = actions!;
        list.Should().ContainSingle();
        list[0].ActionKind.Should().Be(ControlFacade.ActionKindCancel);
    }

    [Fact]
    public async Task GetRunActionsAsync_WhenUnauthorized_ThrowsUnauthorized()
    {
        var db = NewDb();
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = "run-unauth",
            WorkflowInstanceId = "wf-i1",
            DefinitionId = "wf-deny",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = "run-unauth",
            WorkflowInstanceId = "wf-i1",
            WorkflowDefinitionId = "wf-deny",
            Status = WorkflowLifecycleStatusNames.Running,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new RunActionAvailabilityQueryService(
            db,
            new RunKindResolver(db),
            NewProjection(),
            new DenyWorkflowReadAuthz(),
            new AllowOperationReadAuthz());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await service.GetRunActionsAsync(new OperationRunId("run-unauth")));
    }

    private static ControlActionAvailabilityProjection NewProjection()
    {
        return new ControlActionAvailabilityProjection(
            principal: new StubPrincipal(),
            authorization: new AllowAuthorizationEvaluationService(),
            environment: new StubEnvironment("env-1"),
            operationCancelAuthz: new AllowOperationCancelAuthz(),
            workflowCancelAuthz: new AllowWorkflowCancelAuthz(),
            workflowRetryAuthz: new AllowWorkflowRetryAuthz(),
            workflowExternalControlAuthz: new AllowWorkflowExternalControlAuthz(),
            workflowStartAuthz: new AllowWorkflowStartAuthz());
    }

    private static AppertureDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(options);
    }

    private sealed class StubPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new StubActor());
    }

    private sealed class StubActor : IActor
    {
        public ActorId Id => new("actor-1");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Test Actor";
    }

    private sealed class StubEnvironment : IEnvironmentContext
    {
        public StubEnvironment(string environmentId) => EnvironmentId = environmentId;
        public string EnvironmentId { get; }
    }

    private sealed class AllowAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class AllowOperationCancelAuthz : IOperationRunCancelAuthorizationService
    {
        public ValueTask EnsureCanCancelRunAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class AllowWorkflowCancelAuthz : IWorkflowRunCancelAuthorizationService
    {
        public ValueTask EnsureCanCancelRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class AllowWorkflowRetryAuthz : IWorkflowRunRetryAuthorizationService
    {
        public ValueTask EnsureCanRetryRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class AllowWorkflowExternalControlAuthz : IWorkflowExternalControlAuthorizationService
    {
        public ValueTask EnsureCanControlRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class AllowWorkflowStartAuthz : IWorkflowStartAuthorizationService
    {
        public ValueTask EnsureCanStartWorkflowAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class AllowWorkflowReadAuthz : IWorkflowRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class DenyWorkflowReadAuthz : IWorkflowRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException();
    }

    private sealed class AllowOperationReadAuthz : IOperationRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
