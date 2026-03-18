using Apperture.ControlPlane.Security;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests;

public sealed class RunsControllerTests
{
    [Fact]
    public async Task GetRun_WhenMissing_Returns404()
    {
        var controller = new RunsController(new StubRunQueryService(get: _ => null), new StubCancelService(), new StubRetryService());
        var result = await controller.GetRun("missing", CancellationToken.None);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetRuns_ReturnsList()
    {
        var controller = new RunsController(new StubRunQueryService(
            list: new[] { new RunSummaryDto("run-1", "inst-1", "wf-1", "requested", DateTimeOffset.UtcNow, null, null, null) }),
            new StubCancelService(),
            new StubRetryService());
        var result = await controller.GetRuns(CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RetryRun_WhenMissing_Returns404()
    {
        var controller = new RunsController(new StubRunQueryService(), new StubCancelService(), new StubRetryService(returnNull: true));
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
                "requested")));
        var result = await controller.RetryRun("original", CancellationToken.None);
        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task RetryRun_WhenUnauthorized_Returns403()
    {
        var controller = new RunsController(new StubRunQueryService(), new StubCancelService(), new StubRetryService(throwUnauthorized: true));
        var result = await controller.RetryRun("original", CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task RetryRun_WhenIneligible_Returns409()
    {
        var controller = new RunsController(new StubRunQueryService(), new StubCancelService(), new StubRetryService(throwConflict: true));
        var result = await controller.RetryRun("original", CancellationToken.None);
        result.Should().BeOfType<ConflictObjectResult>();
    }

    private sealed class StubRunQueryService : IRunQueryService
    {
        private readonly IReadOnlyList<RunSummaryDto> _list;
        private readonly Func<string, RunDetailDto?> _get;

        public StubRunQueryService(IReadOnlyList<RunSummaryDto>? list = null, Func<string, RunDetailDto?>? get = null)
        {
            _list = list ?? Array.Empty<RunSummaryDto>();
            _get = get ?? (_ => new RunDetailDto("run-1", "inst-1", "wf-1", "requested", DateTimeOffset.UtcNow, null, null, null));
        }

        public ValueTask<IReadOnlyList<RunSummaryDto>> ListRunsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_list);

        public ValueTask<RunDetailDto?> GetRunAsync(Apperture.RuntimePlane.Abstractions.OperationRunId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_get(id.Value));
    }

    private sealed class StubCancelService : IWorkflowCancelService
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
}

