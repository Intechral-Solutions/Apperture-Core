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
        var controller = new RunsController(new StubRunQueryService(get: _ => null));
        var result = await controller.GetRun("missing", CancellationToken.None);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetRuns_ReturnsList()
    {
        var controller = new RunsController(new StubRunQueryService(
            list: new[] { new RunSummaryDto("run-1", "inst-1", "wf-1", "requested", DateTimeOffset.UtcNow, null, null) }));
        var result = await controller.GetRuns(CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    private sealed class StubRunQueryService : IRunQueryService
    {
        private readonly IReadOnlyList<RunSummaryDto> _list;
        private readonly Func<string, RunDetailDto?> _get;

        public StubRunQueryService(IReadOnlyList<RunSummaryDto>? list = null, Func<string, RunDetailDto?>? get = null)
        {
            _list = list ?? Array.Empty<RunSummaryDto>();
            _get = get ?? (_ => new RunDetailDto("run-1", "inst-1", "wf-1", "requested", DateTimeOffset.UtcNow, null, null));
        }

        public ValueTask<IReadOnlyList<RunSummaryDto>> ListRunsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_list);

        public ValueTask<RunDetailDto?> GetRunAsync(Apperture.RuntimePlane.Abstractions.OperationRunId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_get(id.Value));
    }
}

