using Apperture.ControlPlane.Security;
using Apperture.Management.Api.Controllers;
using Apperture.RuntimePlane.Abstractions;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests;

public sealed class RunsControllerTests
{
    [Fact]
    public async Task GetRun_WhenMissing_Returns404()
    {
        var controller = NewController(new StubRunQueryService(get: _ => null));
        var result = await controller.GetRun("missing", CancellationToken.None);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetRuns_ReturnsList()
    {
        var controller = NewController(new StubRunQueryService(
            list: new[] { new RunSummaryDto("run-1", "inst-1", "wf-1", "requested", DateTimeOffset.UtcNow, null, null, null) }));
        var result = await controller.GetRuns(null, null, null, null, null, null, null, CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RetryRun_WhenMissing_Returns404()
    {
        var controller = NewController(new StubRunQueryService(), retry: new StubRetryService(returnNull: true));
        var result = await controller.RetryRun("missing", CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task RetryRun_WhenAuthorized_Returns202()
    {
        var controller = NewController(
            new StubRunQueryService(),
            retry: new StubRetryService(new WorkflowRetryResult(
                new Apperture.RuntimePlane.Abstractions.OperationRunId("original"),
                new Apperture.RuntimePlane.Abstractions.OperationRunId("new-run"),
                new Apperture.RuntimePlane.Abstractions.WorkflowInstanceId("new-instance"),
                new Apperture.RuntimePlane.Abstractions.WorkflowDefinitionId("wf-1"),
                "requested")));
        var result = await controller.RetryRun("original", CancellationToken.None);
        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task RetryRun_WhenUnauthorized_Returns403()
    {
        var controller = NewController(new StubRunQueryService(), retry: new StubRetryService(throwUnauthorized: true));
        var result = await controller.RetryRun("original", CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task RetryRun_WhenIneligible_Returns409()
    {
        var controller = NewController(new StubRunQueryService(), retry: new StubRetryService(throwConflict: true));
        var result = await controller.RetryRun("original", CancellationToken.None);
        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task RetryRun_WhenBlockedByLifecycle_Returns409WithBlockedFlag()
    {
        var blockedResult = new WorkflowRetryResult(
            new OperationRunId("original"),
            RunId: null,
            WorkflowInstanceId: null,
            new WorkflowDefinitionId("wf-1"),
            "blocked",
            IsBlocked: true,
            BlockedReason: "Workflow definition is not executable (module disabled).");
        var controller = NewController(new StubRunQueryService(), retry: new StubRetryService(result: blockedResult));
        var result = await controller.RetryRun("original", CancellationToken.None);
        result.Should().BeOfType<ConflictObjectResult>();
        var conflict = (ConflictObjectResult)result;
        var value = conflict.Value?.GetType().GetProperty("blocked")?.GetValue(conflict.Value);
        value.Should().Be(true);
    }

    [Fact]
    public async Task ParkRun_WhenAuthorized_Returns202()
    {
        var controller = NewController(new StubRunQueryService());
        var result = await controller.ParkRun("run-1", CancellationToken.None);
        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task ResumeRun_WhenAuthorized_Returns202()
    {
        var controller = NewController(new StubRunQueryService());
        var result = await controller.ResumeRun("run-1", CancellationToken.None);
        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task ParkRun_WhenUnauthorized_Returns403()
    {
        var controller = NewController(
            new StubRunQueryService(),
            external: new StubExternalControlService(throwParkUnauthorized: true));
        var result = await controller.ParkRun("run-1", CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ResumeRun_WhenUnauthorized_Returns403()
    {
        var controller = NewController(
            new StubRunQueryService(),
            external: new StubExternalControlService(throwResumeUnauthorized: true));
        var result = await controller.ResumeRun("run-1", CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetRunActions_WhenMissing_Returns404()
    {
        var controller = NewController(new StubRunQueryService(), runActions: new StubRunActionAvailabilityQueryService(result: null));
        var result = await controller.GetRunActions("missing", CancellationToken.None);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetRunActions_WhenUnauthorized_Returns403()
    {
        var controller = NewController(new StubRunQueryService(), runActions: new StubRunActionAvailabilityQueryService(throwUnauthorized: true));
        var result = await controller.GetRunActions("run-1", CancellationToken.None);
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetRunActions_WhenAvailable_Returns200()
    {
        var controller = NewController(
            new StubRunQueryService(),
            runActions: new StubRunActionAvailabilityQueryService(new[]
            {
                new Apperture.Core.Application.ControlFacade.ControlActionAvailabilityDto("execution.cancel", "available", null)
            }));
        var result = await controller.GetRunActions("run-1", CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetRunTimeline_WhenMissing_Returns404()
    {
        var controller = NewController(new StubRunQueryService(), timeline: new StubRunTimelineQueryService(result: null));
        var result = await controller.GetRunTimeline("missing", CancellationToken.None);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetRunTimeline_WhenUnauthorized_Returns403()
    {
        var controller = NewController(new StubRunQueryService(), timeline: new StubRunTimelineQueryService(throwUnauthorized: true));
        var result = await controller.GetRunTimeline("run-1", CancellationToken.None);
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetRunTimeline_WhenAvailable_Returns200()
    {
        var controller = NewController(
            new StubRunQueryService(),
            timeline: new StubRunTimelineQueryService(new RunTimelineDto(
                RunId: "run-1",
                ExecutionKind: "workflow",
                Events: new[]
                {
                    new RunTimelineEventDto(
                        EventId: "startRequest:1:requested",
                        RunId: "run-1",
                        OccurredAt: DateTimeOffset.UtcNow,
                        EventKind: RunTimeline.EventKindRunRequested,
                        Summary: "Run requested.",
                        Actor: null,
                        SourceType: RunTimeline.SourceTypeStartRequest,
                        SourceId: "1",
                        Metadata: new Dictionary<string, string?>())
                })));
        var result = await controller.GetRunTimeline("run-1", CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    private static RunsController NewController(
        IRunQueryService runs,
        IExecutionCancelService? cancel = null,
        IWorkflowRetryService? retry = null,
        IWorkflowExternalControlService? external = null,
        IRunActionAvailabilityQueryService? runActions = null,
        IRunTimelineQueryService? timeline = null) =>
        new(
            runs,
            cancel ?? new StubCancelService(),
            retry ?? new StubRetryService(),
            external ?? new StubExternalControlService(),
            runActions ?? new StubRunActionAvailabilityQueryService(new[]
            {
                new Apperture.Core.Application.ControlFacade.ControlActionAvailabilityDto("execution.cancel", "available", null)
            }),
            timeline ?? new StubRunTimelineQueryService(new RunTimelineDto("run-1", "workflow", Array.Empty<RunTimelineEventDto>())));

    private sealed class StubRunQueryService : IRunQueryService
    {
        private readonly IReadOnlyList<RunSummaryDto> _list;
        private readonly Func<string, RunDetailDto?> _get;

        public StubRunQueryService(IReadOnlyList<RunSummaryDto>? list = null, Func<string, RunDetailDto?>? get = null)
        {
            _list = list ?? Array.Empty<RunSummaryDto>();
            _get = get ?? (_ => new RunDetailDto(
                "run-1",
                "inst-1",
                "wf-1",
                "requested",
                DateTimeOffset.UtcNow,
                null,
                null,
                null,
                null,
                new RunResultDto("in_progress", "Execution is still in progress.", null, null),
                null,
                Array.Empty<RunLifecycleEventDto>(),
                Array.Empty<RunStepInstanceDto>(),
                Array.Empty<RunControlRequestDto>(),
                null,
                null,
                null,
                null,
                null,
                null));
        }

        public ValueTask<IReadOnlyList<RunSummaryDto>> ListRunsAsync(RunQueryFilter? filter = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_list);

        public ValueTask<RunDetailDto?> GetRunAsync(Apperture.RuntimePlane.Abstractions.OperationRunId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_get(id.Value));
    }

    private sealed class StubRunActionAvailabilityQueryService : IRunActionAvailabilityQueryService
    {
        private readonly IReadOnlyList<Apperture.Core.Application.ControlFacade.ControlActionAvailabilityDto>? _result;
        private readonly bool _throwUnauthorized;

        public StubRunActionAvailabilityQueryService(
            IReadOnlyList<Apperture.Core.Application.ControlFacade.ControlActionAvailabilityDto>? result = null,
            bool throwUnauthorized = false)
        {
            _result = result;
            _throwUnauthorized = throwUnauthorized;
        }

        public Task<IReadOnlyList<Apperture.Core.Application.ControlFacade.ControlActionAvailabilityDto>?> GetRunActionsAsync(
            Apperture.RuntimePlane.Abstractions.OperationRunId runId,
            CancellationToken cancellationToken = default)
        {
            if (_throwUnauthorized)
                throw new UnauthorizedAccessException();

            return Task.FromResult(_result);
        }
    }

    private sealed class StubRunTimelineQueryService : IRunTimelineQueryService
    {
        private readonly RunTimelineDto? _result;
        private readonly bool _throwUnauthorized;

        public StubRunTimelineQueryService(RunTimelineDto? result = null, bool throwUnauthorized = false)
        {
            _result = result;
            _throwUnauthorized = throwUnauthorized;
        }

        public Task<RunTimelineDto?> GetRunTimelineAsync(OperationRunId runId, CancellationToken cancellationToken = default)
        {
            if (_throwUnauthorized)
                throw new UnauthorizedAccessException();
            return Task.FromResult(_result);
        }
    }

    private sealed class StubCancelService : IExecutionCancelService
    {
        public ValueTask<bool> RequestCancelAsync(
            Apperture.RuntimePlane.Abstractions.OperationRunId runId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class StubRetryService : IWorkflowRetryService
    {
        private readonly WorkflowRetryResult? _result;
        private readonly bool _throwUnauthorized;
        private readonly bool _returnNull;
        private readonly bool _throwConflict;

        public StubRetryService(WorkflowRetryResult? result = null, bool throwUnauthorized = false, bool returnNull = false, bool throwConflict = false)
        {
            _result = result ?? new WorkflowRetryResult(
                new Apperture.RuntimePlane.Abstractions.OperationRunId("original"),
                new Apperture.RuntimePlane.Abstractions.OperationRunId("new-run"),
                new Apperture.RuntimePlane.Abstractions.WorkflowInstanceId("new-instance"),
                new Apperture.RuntimePlane.Abstractions.WorkflowDefinitionId("wf-1"),
                "requested");
            _throwUnauthorized = throwUnauthorized;
            _returnNull = returnNull;
            _throwConflict = throwConflict;
        }

        public ValueTask<WorkflowRetryResult?> RequestRetryAsync(
            Apperture.RuntimePlane.Abstractions.OperationRunId runId,
            CancellationToken cancellationToken = default)
        {
            if (_throwUnauthorized)
                throw new UnauthorizedAccessException("deny");
            if (_throwConflict)
                throw new InvalidOperationException("not eligible");
            if (_returnNull)
                return ValueTask.FromResult<WorkflowRetryResult?>(null);
            return ValueTask.FromResult(_result);
        }
    }

    private sealed class StubExternalControlService : IWorkflowExternalControlService
    {
        private readonly bool _throwParkUnauthorized;
        private readonly bool _throwResumeUnauthorized;

        public StubExternalControlService(bool throwParkUnauthorized = false, bool throwResumeUnauthorized = false)
        {
            _throwParkUnauthorized = throwParkUnauthorized;
            _throwResumeUnauthorized = throwResumeUnauthorized;
        }

        public ValueTask<WorkflowExternalControlResult?> RequestParkAsync(Apperture.RuntimePlane.Abstractions.OperationRunId runId, CancellationToken cancellationToken = default)
        {
            if (_throwParkUnauthorized)
                throw new UnauthorizedAccessException("deny");
            return ValueTask.FromResult<WorkflowExternalControlResult?>(new WorkflowExternalControlResult(
                runId,
                new Apperture.RuntimePlane.Abstractions.WorkflowInstanceId("inst-1"),
                new Apperture.RuntimePlane.Abstractions.WorkflowDefinitionId("wf-1"),
                WorkflowExternalControlKindNames.Park,
                "queued",
                WorkflowLifecycleStatusNames.Running,
                "Step1"));
        }

        public ValueTask<WorkflowExternalControlResult?> RequestResumeAsync(Apperture.RuntimePlane.Abstractions.OperationRunId runId, CancellationToken cancellationToken = default)
        {
            if (_throwResumeUnauthorized)
                throw new UnauthorizedAccessException("deny");
            return ValueTask.FromResult<WorkflowExternalControlResult?>(new WorkflowExternalControlResult(
                runId,
                new Apperture.RuntimePlane.Abstractions.WorkflowInstanceId("inst-1"),
                new Apperture.RuntimePlane.Abstractions.WorkflowDefinitionId("wf-1"),
                WorkflowExternalControlKindNames.Resume,
                "queued",
                WorkflowLifecycleStatusNames.Parked,
                "Step2"));
        }
    }
}

