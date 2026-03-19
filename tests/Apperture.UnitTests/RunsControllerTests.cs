using Apperture.ControlPlane.Security;
using Apperture.Management.Api.Controllers;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests;

public sealed class RunsControllerTests
{
    [Fact]
    public async Task GetRun_WhenMissing_Returns404()
    {
        var controller = new RunsController(new StubRunQueryService(get: _ => null), new StubCancelService(), new StubRetryService(), new StubExternalControlService());
        var result = await controller.GetRun("missing", CancellationToken.None);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetRuns_ReturnsList()
    {
        var controller = new RunsController(new StubRunQueryService(
            list: new[] { new RunSummaryDto("run-1", "inst-1", "wf-1", "requested", DateTimeOffset.UtcNow, null, null, null) }),
            new StubCancelService(),
            new StubRetryService(),
            new StubExternalControlService());
        var result = await controller.GetRuns(CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RetryRun_WhenMissing_Returns404()
    {
        var controller = new RunsController(new StubRunQueryService(), new StubCancelService(), new StubRetryService(returnNull: true), new StubExternalControlService());
        var result = await controller.RetryRun("missing", CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task RetryRun_WhenAuthorized_Returns202()
    {
        var controller = new RunsController(
            new StubRunQueryService(),
            new StubCancelService(),
            new StubRetryService(new WorkflowRetryResult(
                new Apperture.RuntimePlane.Abstractions.OperationRunId("original"),
                new Apperture.RuntimePlane.Abstractions.OperationRunId("new-run"),
                new Apperture.RuntimePlane.Abstractions.WorkflowInstanceId("new-instance"),
                new Apperture.RuntimePlane.Abstractions.WorkflowDefinitionId("wf-1"),
                "requested")),
            new StubExternalControlService());
        var result = await controller.RetryRun("original", CancellationToken.None);
        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task RetryRun_WhenUnauthorized_Returns403()
    {
        var controller = new RunsController(new StubRunQueryService(), new StubCancelService(), new StubRetryService(throwUnauthorized: true), new StubExternalControlService());
        var result = await controller.RetryRun("original", CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task RetryRun_WhenIneligible_Returns409()
    {
        var controller = new RunsController(new StubRunQueryService(), new StubCancelService(), new StubRetryService(throwConflict: true), new StubExternalControlService());
        var result = await controller.RetryRun("original", CancellationToken.None);
        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task ParkRun_WhenAuthorized_Returns202()
    {
        var controller = new RunsController(new StubRunQueryService(), new StubCancelService(), new StubRetryService(), new StubExternalControlService());
        var result = await controller.ParkRun("run-1", CancellationToken.None);
        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task ResumeRun_WhenAuthorized_Returns202()
    {
        var controller = new RunsController(new StubRunQueryService(), new StubCancelService(), new StubRetryService(), new StubExternalControlService());
        var result = await controller.ResumeRun("run-1", CancellationToken.None);
        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task ParkRun_WhenUnauthorized_Returns403()
    {
        var controller = new RunsController(
            new StubRunQueryService(),
            new StubCancelService(),
            new StubRetryService(),
            new StubExternalControlService(throwParkUnauthorized: true));
        var result = await controller.ParkRun("run-1", CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ResumeRun_WhenUnauthorized_Returns403()
    {
        var controller = new RunsController(
            new StubRunQueryService(),
            new StubCancelService(),
            new StubRetryService(),
            new StubExternalControlService(throwResumeUnauthorized: true));
        var result = await controller.ResumeRun("run-1", CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
    }

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
                Array.Empty<RunLifecycleEventDto>(),
                Array.Empty<RunStepInstanceDto>(),
                Array.Empty<RunControlRequestDto>()));
        }

        public ValueTask<IReadOnlyList<RunSummaryDto>> ListRunsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_list);

        public ValueTask<RunDetailDto?> GetRunAsync(Apperture.RuntimePlane.Abstractions.OperationRunId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_get(id.Value));
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

