using System.Globalization;
using Apperture.Core.Application.ControlFacade;
using Apperture.ControlPlane.Security;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Trust;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class UnifiedControlFacadeTests
{
    [Fact]
    public void TryParseScheduleId_ExtractsScheduleId()
    {
        ControlFacade.TryParseScheduleId("foo schedule:sc-1 bar", out var id).Should().BeTrue();
        id.Should().Be("sc-1");
    }

    [Theory]
    [InlineData("requested", ControlFacade.NormalizedRequested)]
    [InlineData("running", ControlFacade.NormalizedRunning)]
    [InlineData("parked", ControlFacade.NormalizedParked)]
    [InlineData("completed", ControlFacade.NormalizedSucceeded)]
    [InlineData("failed", ControlFacade.NormalizedFailed)]
    [InlineData("cancelled", ControlFacade.NormalizedCancelled)]
    public void NormalizeRunStatus_MapsWorkflowStates(string raw, string expected)
    {
        ControlFacade.NormalizeRunStatus(raw).Should().Be(expected);
    }

    [Fact]
    public async Task ActionProjection_ResumeNotEligible_WhenNotParked()
    {
        var projection = NewProjection(allowCancel: true, allowExternalControl: true, allowRetry: true);

        var run = new RunSummaryDto(
            RunId: "r1",
            ExecutionInstanceId: "i1",
            ExecutionDefinitionId: "wf-1",
            Status: WorkflowLifecycleStatusNames.Running,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow");

        var actions = await projection.ProjectExecutionRunActionsAsync(run, hasQueuedPark: false, hasQueuedResume: false);

        var resume = actions.Single(a => a.ActionKind == ControlFacade.ActionKindResume);
        resume.AvailabilityState.Should().Be(ControlFacade.ActionAvailabilityNotEligible);
        resume.ReasonCode.Should().Be(ControlFacade.ReasonStateNotEligible);
    }

    [Fact]
    public async Task ActionProjection_ResumeAlreadyQueued_WhenQueued()
    {
        var projection = NewProjection(allowCancel: true, allowExternalControl: false, allowRetry: true);

        var run = new RunSummaryDto(
            RunId: "r1",
            ExecutionInstanceId: "i1",
            ExecutionDefinitionId: "wf-1",
            Status: WorkflowLifecycleStatusNames.Parked,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow");

        var actions = await projection.ProjectExecutionRunActionsAsync(run, hasQueuedPark: false, hasQueuedResume: true);

        var resume = actions.Single(a => a.ActionKind == ControlFacade.ActionKindResume);
        resume.AvailabilityState.Should().Be(ControlFacade.ActionAvailabilityAlreadyQueued);
        resume.ReasonCode.Should().Be(ControlFacade.ReasonAlreadyQueued);
    }

    [Fact]
    public async Task ActionProjection_CancelNotEligible_WhenTerminal()
    {
        var projection = NewProjection(allowCancel: true, allowExternalControl: true, allowRetry: true);

        var run = new RunSummaryDto(
            RunId: "r1",
            ExecutionInstanceId: "i1",
            ExecutionDefinitionId: "wf-1",
            Status: WorkflowLifecycleStatusNames.Completed,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow");

        var actions = await projection.ProjectExecutionRunActionsAsync(run, hasQueuedPark: false, hasQueuedResume: false);

        var cancel = actions.Single(a => a.ActionKind == ControlFacade.ActionKindCancel);
        cancel.AvailabilityState.Should().Be(ControlFacade.ActionAvailabilityNotEligible);
        cancel.ReasonCode.Should().Be(ControlFacade.ReasonStateNotEligible);
    }

    [Fact]
    public async Task ControlActivityQuery_SetsScheduleTarget_WhenCorrelationHasScheduleToken()
    {
        var db = NewDb();
        var runQuery = new StubRunQueryService(new[]
        {
            new RunSummaryDto(
                RunId: "run-1",
                ExecutionInstanceId: "i1",
                ExecutionDefinitionId: "wf-1",
                Status: WorkflowLifecycleStatusNames.Failed,
                CreatedAt: new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero),
                CompletedAt: new DateTimeOffset(2026, 3, 20, 10, 1, 0, TimeSpan.Zero),
                CorrelationKey: "schedule:sc-1",
                RetryOfRunId: null,
                ExecutionKind: "workflow")
        });

        var service = new ControlActivityQueryService(
            runs: runQuery,
            runtimeIdentity: new StubRuntimeIdentityService(Array.Empty<Apperture.Core.Security.Trust.RuntimeIdentity>()),
            runtimeControl: new StubRuntimeControlService(),
            actions: new StubExecutionActionProjection(),
            authorization: new AllowAuthorizationEvaluationService(),
            principal: new StubPrincipal(),
            environment: new StubEnvironment("env-1"),
            db: db);

        var items = await service.ListRecentActivityAsync(createdFromUtc: DateTimeOffset.UtcNow.AddHours(-1), take: 5);
        items.Should().NotBeEmpty();

        var item = items.Single();
        item.ActivitySource.Should().Be(ControlFacade.ActivitySourceScheduled);
        item.TargetKind.Should().Be(ControlFacade.TargetKindSchedule);
        item.TargetId.Should().Be("sc-1");
        item.CurrentNormalizedStatus.Should().Be(ControlFacade.NormalizedFailed);
        item.Outcome.Should().Be("failed");
    }

    [Fact]
    public async Task ControlAttentionQuery_UsesScheduleTarget_ForFailedExecution()
    {
        var db = NewDb();
        var failedRuns = new[]
        {
            new RunSummaryDto(
                RunId: "run-1",
                ExecutionInstanceId: "i1",
                ExecutionDefinitionId: "wf-1",
                Status: WorkflowLifecycleStatusNames.Failed,
                CreatedAt: new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero),
                CompletedAt: null,
                CorrelationKey: "schedule:sc-1",
                RetryOfRunId: null,
                ExecutionKind: "workflow")
        };

        var service = new ControlAttentionQueryService(
            runs: new StubRunQueryService(failedRuns),
            authorization: new AllowAuthorizationEvaluationService(),
            principal: new StubPrincipal(),
            environment: new StubEnvironment("env-1"),
            db: db,
            workflowRunReadAuthz: new AllowWorkflowReadAuthz());

        var items = await service.ListAttentionAsync(createdFromUtc: DateTimeOffset.UtcNow.AddHours(-1), take: 10);
        items.Should().ContainSingle();

        var item = items.Single();
        item.AttentionKind.Should().Be("executionFailed");
        item.TargetKind.Should().Be(ControlFacade.TargetKindSchedule);
        item.TargetId.Should().Be("sc-1");
    }

    private static Apperture.ControlPlane.Security.ControlActionAvailabilityProjection NewProjection(bool allowCancel, bool allowExternalControl, bool allowRetry)
    {
        return new Apperture.ControlPlane.Security.ControlActionAvailabilityProjection(
            principal: new StubPrincipal(),
            authorization: new AllowAuthorizationEvaluationService(),
            environment: new StubEnvironment("env-1"),
            operationCancelAuthz: new StubOperationRunCancelAuthz(allowCancel),
            workflowCancelAuthz: new StubWorkflowRunCancelAuthz(allowCancel),
            workflowRetryAuthz: new StubWorkflowRunRetryAuthz(allowRetry),
            workflowExternalControlAuthz: new StubWorkflowExternalControlAuthz(allowExternalControl),
            workflowStartAuthz: new StubWorkflowStartAuthz(allow: true));
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private sealed class StubExecutionActionProjection : IControlActionAvailabilityProjection
    {
        public ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectExecutionRunActionsAsync(
            RunSummaryDto run,
            bool hasQueuedPark,
            bool hasQueuedResume,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlActionAvailabilityDto>>(Array.Empty<ControlActionAvailabilityDto>());

        public ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectOperationDefinitionActionsAsync(
            OperationDefinitionDto operation,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlActionAvailabilityDto>>(Array.Empty<ControlActionAvailabilityDto>());

        public ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectWorkflowDefinitionActionsAsync(
            WorkflowDefinition workflow,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlActionAvailabilityDto>>(Array.Empty<ControlActionAvailabilityDto>());

        public ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectRuntimeInstanceActionsAsync(
            Apperture.Core.Security.Trust.RuntimeIdentity runtime,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlActionAvailabilityDto>>(Array.Empty<ControlActionAvailabilityDto>());
    }

    private sealed class StubRunQueryService : IRunQueryService
    {
        private readonly IReadOnlyList<RunSummaryDto> _runs;

        public StubRunQueryService(IReadOnlyList<RunSummaryDto> runs) => _runs = runs;

        public ValueTask<IReadOnlyList<RunSummaryDto>> ListRunsAsync(RunQueryFilter? filter = null, CancellationToken cancellationToken = default)
        {
            if (filter?.Status is null)
                return ValueTask.FromResult(_runs);

            var wanted = filter.Status;
            var list = _runs.Where(r => string.Equals(r.Status, wanted, StringComparison.Ordinal)).ToList();
            return ValueTask.FromResult<IReadOnlyList<RunSummaryDto>>(list);
        }

        public ValueTask<RunDetailDto?> GetRunAsync(OperationRunId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RunDetailDto?>(null);
    }

    private sealed class StubRuntimeControlService : IRuntimeControlService
    {
        public ValueTask<RuntimeControlRequestResult?> RequestShutdownAsync(string runtimeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimeControlRequestResult?>(null);

        public ValueTask<IReadOnlyList<RuntimeControlRequestResult>> GetShutdownRequestsAsync(string runtimeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<RuntimeControlRequestResult>>(Array.Empty<RuntimeControlRequestResult>());
    }

    private sealed class StubRuntimeIdentityService : IRuntimeIdentityService
    {
        private readonly IReadOnlyList<Apperture.Core.Security.Trust.RuntimeIdentity> _runtimes;

        public StubRuntimeIdentityService(IReadOnlyList<Apperture.Core.Security.Trust.RuntimeIdentity> runtimes) => _runtimes = runtimes;

        public ValueTask<Apperture.Core.Security.Trust.RuntimeIdentity?> GetByIdAsync(string runtimeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Apperture.Core.Security.Trust.RuntimeIdentity?>(null);

        public ValueTask<IReadOnlyList<Apperture.Core.Security.Trust.RuntimeIdentity>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_runtimes);

        public ValueTask<Apperture.Core.Security.Trust.RuntimeTrustValidationResult> ValidateAsync(string runtimeId, string presentedCredential, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<Apperture.Core.Security.Trust.EnrollmentResult> RequestCredentialRolloverAsync(Apperture.Core.Security.Trust.RuntimeCredentialRolloverRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class AllowAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
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
        public string? DisplayName => "Test";
    }

    private sealed class StubEnvironment : IEnvironmentContext
    {
        public StubEnvironment(string environmentId) => EnvironmentId = environmentId;
        public string EnvironmentId { get; }
    }

    private sealed class AllowWorkflowReadAuthz : IWorkflowRunReadAuthorizationService
    {
        public ValueTask EnsureCanReadRunsAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubOperationRunCancelAuthz : IOperationRunCancelAuthorizationService
    {
        private readonly bool _allow;

        public StubOperationRunCancelAuthz(bool allow) => _allow = allow;

        public ValueTask EnsureCanCancelRunAsync(OperationId operationId, CancellationToken cancellationToken = default)
        {
            if (!_allow) throw new UnauthorizedAccessException();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubWorkflowRunCancelAuthz : IWorkflowRunCancelAuthorizationService
    {
        private readonly bool _allow;

        public StubWorkflowRunCancelAuthz(bool allow) => _allow = allow;

        public ValueTask EnsureCanCancelRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            if (!_allow) throw new UnauthorizedAccessException();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubWorkflowRunRetryAuthz : IWorkflowRunRetryAuthorizationService
    {
        private readonly bool _allow;

        public StubWorkflowRunRetryAuthz(bool allow) => _allow = allow;

        public ValueTask EnsureCanRetryRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            if (!_allow) throw new UnauthorizedAccessException();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubWorkflowExternalControlAuthz : IWorkflowExternalControlAuthorizationService
    {
        private readonly bool _allow;
        public StubWorkflowExternalControlAuthz(bool allow) => _allow = allow;

        public ValueTask EnsureCanControlRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            if (!_allow) throw new UnauthorizedAccessException();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubWorkflowStartAuthz : IWorkflowStartAuthorizationService
    {
        private readonly bool _allow;
        public StubWorkflowStartAuthz(bool allow) => _allow = allow;

        public ValueTask EnsureCanStartWorkflowAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            if (!_allow) throw new UnauthorizedAccessException();
            return ValueTask.CompletedTask;
        }
    }
}

