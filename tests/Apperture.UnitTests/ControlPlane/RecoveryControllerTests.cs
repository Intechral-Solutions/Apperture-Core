using Apperture.Core.Application.Recovery;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Management.Api.Controllers;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class RecoveryControllerTests
{
    [Fact]
    public async Task ListBlockedWork_Returns200()
    {
        var controller = NewController(recovery: new StubRecoveryQueryService(
            blocked: [CreateBlockedEntry("r1", "workflow", true)]));
        var result = await controller.ListBlockedWork(cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetBlockedWork_WhenMissing_Returns404()
    {
        var controller = NewController(recovery: new StubRecoveryQueryService(blocked: []));
        var result = await controller.GetBlockedWork("missing", CancellationToken.None);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetBlockedWork_WhenFound_Returns200()
    {
        var entry = CreateBlockedEntry("r1", "workflow", true);
        var controller = NewController(recovery: new StubRecoveryQueryService(blocked: [entry], single: entry));
        var result = await controller.GetBlockedWork("r1", CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RetryBlockedWork_WhenEntryMissing_Returns404()
    {
        var controller = NewController(recovery: new StubRecoveryQueryService(blocked: [], single: null));
        var result = await controller.RetryBlockedWork("missing", CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task RetryBlockedWork_WhenOperation_Returns400()
    {
        var entry = CreateBlockedEntry("r1", "operation", false);
        var controller = NewController(recovery: new StubRecoveryQueryService(blocked: [entry], single: entry));
        var result = await controller.RetryBlockedWork("r1", CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RetryBlockedWork_WhenIneligible_Returns409()
    {
        var entry = CreateBlockedEntry("r1", "workflow", retryEligibleNow: false);
        var controller = NewController(recovery: new StubRecoveryQueryService(blocked: [entry], single: entry));
        var result = await controller.RetryBlockedWork("r1", CancellationToken.None);
        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task RetryBlockedWork_WhenEligibleAndSuccess_Returns202()
    {
        var entry = CreateBlockedEntry("r1", "workflow", true);
        var retryResult = new WorkflowRetryResult(
            new OperationRunId("r1"),
            new OperationRunId("r2"),
            new WorkflowInstanceId("inst-2"),
            new WorkflowDefinitionId("wf-1"),
            "requested");
        var controller = NewController(
            recovery: new StubRecoveryQueryService(blocked: [entry], single: entry),
            retry: new StubRetryService(retryResult));
        var result = await controller.RetryBlockedWork("r1", CancellationToken.None);
        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task RetryBlockedWork_WhenRetryReturnsBlocked_Returns409()
    {
        var entry = CreateBlockedEntry("r1", "workflow", true);
        var blockedResult = new WorkflowRetryResult(
            new OperationRunId("r1"), null, null, new WorkflowDefinitionId("wf-1"), "blocked",
            IsBlocked: true, BlockedReason: "Module disabled");
        var controller = NewController(
            recovery: new StubRecoveryQueryService(blocked: [entry], single: entry),
            retry: new StubRetryService(blockedResult));
        var result = await controller.RetryBlockedWork("r1", CancellationToken.None);
        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task GetScheduleRemediation_WhenMissing_Returns404()
    {
        var controller = NewController(recovery: new StubRecoveryQueryService(blocked: [], schedule: null));
        var result = await controller.GetScheduleRemediation("s1", cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetScheduleRemediation_WhenFound_Returns200()
    {
        var schedule = new ScheduleRemediationEntry("s1", "MySchedule", true, "BlockedByLifecycle", "reason",
            false, false, DateTimeOffset.UtcNow.AddHours(1), "Next due at...");
        var controller = NewController(recovery: new StubRecoveryQueryService(blocked: [], schedule: schedule));
        var result = await controller.GetScheduleRemediation("s1", cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RunScheduleCycle_Returns202()
    {
        var runner = new StubScheduleDispatchRunner();
        var controller = NewController(scheduleRunner: runner);
        var result = await controller.RunScheduleCycle(cancellationToken: CancellationToken.None);
        result.Should().BeOfType<AcceptedResult>();
        runner.RunCycleCalled.Should().BeTrue();
    }

    [Fact]
    public async Task GetModuleRemediation_WhenMissing_Returns404()
    {
        var controller = NewController(recovery: new StubRecoveryQueryService(blocked: [], module: null));
        var result = await controller.GetModuleRemediation("m1", CancellationToken.None);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetModuleRemediation_WhenFound_Returns200()
    {
        var module = new ModuleRemediationEntry("m1", "InstalledDisabled", false, "invalid_persisted_state",
            2, 1, "Enable module to allow new starts.");
        var controller = NewController(recovery: new StubRecoveryQueryService(blocked: [], module: module));
        var result = await controller.GetModuleRemediation("m1", CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    private static BlockedWorkRemediationEntry CreateBlockedEntry(string runId, string kind, bool retryEligibleNow) =>
        new(runId, "def-1", kind, "m1", DateTimeOffset.UtcNow.AddHours(-1),
            "blocked reason", retryEligibleNow, retryEligibleNow ? "eligible" : "not eligible");

    private static RecoveryController NewController(
        IRecoveryQueryService? recovery = null,
        IWorkflowRetryService? retry = null,
        IScheduleDispatchRunner? scheduleRunner = null,
        IPrincipalContext? principal = null,
        IAuditService? audit = null)
    {
        return new RecoveryController(
            recovery ?? new StubRecoveryQueryService(blocked: []),
            retry ?? new StubRetryService(),
            scheduleRunner ?? new StubScheduleDispatchRunner(),
            principal ?? new StubPrincipalContext(),
            audit);
    }

    private sealed class StubRecoveryQueryService : IRecoveryQueryService
    {
        private readonly IReadOnlyList<BlockedWorkRemediationEntry> _blocked;
        private readonly BlockedWorkRemediationEntry? _single;
        private readonly ScheduleRemediationEntry? _schedule;
        private readonly ModuleRemediationEntry? _module;

        public StubRecoveryQueryService(
            IReadOnlyList<BlockedWorkRemediationEntry>? blocked = null,
            BlockedWorkRemediationEntry? single = null,
            ScheduleRemediationEntry? schedule = null,
            ModuleRemediationEntry? module = null)
        {
            _blocked = blocked ?? [];
            _single = single ?? _blocked.FirstOrDefault();
            _schedule = schedule;
            _module = module;
        }

        public ValueTask<IReadOnlyList<BlockedWorkRemediationEntry>> GetBlockedWorkRemediationAsync(
            string? runtimePlaneId = "local", int windowHours = 24, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_blocked);

        public ValueTask<BlockedWorkRemediationEntry?> GetBlockedWorkRemediationForRunAsync(
            string runId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_single?.RunId == runId ? _single : _blocked.FirstOrDefault(b => b.RunId == runId));

        public ValueTask<ScheduleRemediationEntry?> GetScheduleRemediationAsync(
            string scheduleId, string? runtimePlaneId = "local", CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_schedule);

        public ValueTask<ModuleRemediationEntry?> GetModuleRemediationAsync(
            string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_module);
    }

    private sealed class StubRetryService : IWorkflowRetryService
    {
        private readonly WorkflowRetryResult? _result;

        public StubRetryService(WorkflowRetryResult? result = null) => _result = result;

        public ValueTask<WorkflowRetryResult?> RequestRetryAsync(OperationRunId runId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowRetryResult?>(_result ?? new WorkflowRetryResult(
                runId, new OperationRunId("new"), new WorkflowInstanceId("inst"), new WorkflowDefinitionId("wf"), "requested"));
    }

    private sealed class StubScheduleDispatchRunner : IScheduleDispatchRunner
    {
        public bool RunCycleCalled { get; private set; }

        public ValueTask RunCycleAsync(RuntimePlaneId planeId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default)
        {
            RunCycleCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        public ValueTask<Apperture.Shared.Kernel.IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Apperture.Shared.Kernel.IActor?>(null);
    }
}
