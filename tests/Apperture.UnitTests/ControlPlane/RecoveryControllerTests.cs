using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.Services;
using Apperture.ControlPlane.Security;
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
    public async Task RetryBlockedWork_WhenOperation_Returns400WithInspectionOnly()
    {
        var entry = CreateBlockedEntry("r1", "operation", false);
        var controller = NewController(recovery: new StubRecoveryQueryService(blocked: [entry], single: entry));
        var result = await controller.RetryBlockedWork("r1", CancellationToken.None);
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var value = badRequest.Value;
        value.Should().NotBeNull();
        var inspectionOnly = value!.GetType().GetProperty("inspectionOnly")?.GetValue(value);
        inspectionOnly.Should().Be(true, "operation blocked work is inspection-only; platform recovery does not support operation retry");
        var definitionKind = value.GetType().GetProperty("definitionKind")?.GetValue(value);
        definitionKind.Should().Be("operation");
        var message = value.GetType().GetProperty("message")?.GetValue(value) as string;
        message.Should().Contain("inspectable", "response must tell operator that operation items are inspectable via GET blocked-work");
        message.Should().Contain("not yet support operation retry", "response must explicitly state operation retry limitation");
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
    public async Task RunScheduleCycle_InvokesGlobalSchedulerCycle_Returns202()
    {
        var runner = new StubScheduleDispatchRunner();
        var controller = NewController(scheduleRunner: runner);
        var result = await controller.RunScheduleCycle(cancellationToken: CancellationToken.None);
        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        runner.RunCycleCalled.Should().BeTrue("controller must invoke IScheduleDispatchRunner.RunCycleAsync");
        accepted.Value.Should().NotBeNull();
        var action = accepted.Value!.GetType().GetProperty("action")?.GetValue(accepted.Value);
        action.Should().Be("global_scheduler_run_cycle", "response must indicate global scheduler action");
        var message = accepted.Value!.GetType().GetProperty("message")?.GetValue(accepted.Value) as string;
        message.Should().Contain("not schedule-specific", "response must clarify this is global re-evaluation, not schedule-specific force-fire");
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

    // Epic 34: authorization tests
    [Fact]
    public async Task ListBlockedWork_WithoutRecoveryRead_Returns403()
    {
        var gov = new StubRecoveryGovernanceService(canRead: false, canManage: false);
        var controller = NewController(
            recovery: new StubRecoveryQueryService(blocked: [CreateBlockedEntry("r1", "workflow", true)]),
            recoveryGovernance: gov);
        var result = await controller.ListBlockedWork(cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ListBlockedWork_WithRecoveryRead_Returns200()
    {
        var gov = new StubRecoveryGovernanceService(canRead: true, canManage: false);
        var controller = NewController(
            recovery: new StubRecoveryQueryService(blocked: [CreateBlockedEntry("r1", "workflow", true)]),
            recoveryGovernance: gov);
        var result = await controller.ListBlockedWork(cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RetryBlockedWork_WithoutRecoveryManage_Returns403()
    {
        var entry = CreateBlockedEntry("r1", "workflow", true);
        var gov = new StubRecoveryGovernanceService(canRead: true, canManage: false);
        var controller = NewController(
            recovery: new StubRecoveryQueryService(blocked: [entry], single: entry),
            retry: new StubRetryService(new WorkflowRetryResult(
                new OperationRunId("r1"), new OperationRunId("r2"), new WorkflowInstanceId("i"), new WorkflowDefinitionId("wf"), "requested")),
            recoveryGovernance: gov);
        var result = await controller.RetryBlockedWork("r1", CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task RetryBlockedWork_WithRecoveryManage_WhenEligible_Returns202()
    {
        var entry = CreateBlockedEntry("r1", "workflow", true);
        var gov = new StubRecoveryGovernanceService(canRead: true, canManage: true);
        var controller = NewController(
            recovery: new StubRecoveryQueryService(blocked: [entry], single: entry),
            retry: new StubRetryService(new WorkflowRetryResult(
                new OperationRunId("r1"), new OperationRunId("r2"), new WorkflowInstanceId("i"), new WorkflowDefinitionId("wf"), "requested")),
            recoveryGovernance: gov);
        var result = await controller.RetryBlockedWork("r1", CancellationToken.None);
        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task RunScheduleCycle_WithoutRecoveryManage_Returns403()
    {
        var gov = new StubRecoveryGovernanceService(canRead: true, canManage: false);
        var controller = NewController(recoveryGovernance: gov);
        var result = await controller.RunScheduleCycle(cancellationToken: CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task RunScheduleCycle_WithRecoveryManage_Returns202()
    {
        var gov = new StubRecoveryGovernanceService(canRead: true, canManage: true);
        var runner = new StubScheduleDispatchRunner();
        var controller = NewController(recoveryGovernance: gov, scheduleRunner: runner);
        var result = await controller.RunScheduleCycle(cancellationToken: CancellationToken.None);
        result.Should().BeOfType<AcceptedResult>();
        runner.RunCycleCalled.Should().BeTrue();
    }

    [Fact]
    public async Task RetryBlockedWork_WhenDenied_AuditsRetryDenied()
    {
        var entry = CreateBlockedEntry("r1", "workflow", true);
        var gov = new StubRecoveryGovernanceService(canRead: true, canManage: false);
        var spyAudit = new SpyAuditService();
        var controller = NewController(
            recovery: new StubRecoveryQueryService(blocked: [entry], single: entry),
            recoveryGovernance: gov,
            audit: spyAudit);
        var result = await controller.RetryBlockedWork("r1", CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
        spyAudit.LastAppended.Should().NotBeNull("denial audit policy requires recovery retry denials to be audited");
        spyAudit.LastAppended!.Action.Should().Be(RecoveryAuditActionNames.RetryDenied);
    }

    [Fact]
    public async Task RunScheduleCycle_WhenDenied_AuditsRunCycleDenied()
    {
        var gov = new StubRecoveryGovernanceService(canRead: true, canManage: false);
        var spyAudit = new SpyAuditService();
        var controller = NewController(recoveryGovernance: gov, audit: spyAudit);
        var result = await controller.RunScheduleCycle(cancellationToken: CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
        spyAudit.LastAppended.Should().NotBeNull("denial audit policy requires run-cycle denials to be audited");
        spyAudit.LastAppended!.Action.Should().Be(RecoveryAuditActionNames.RunCycleDenied);
    }

    [Fact]
    public async Task GetScheduleRemediation_WithoutRecoveryRead_Returns403()
    {
        var gov = new StubRecoveryGovernanceService(canRead: false, canManage: false);
        var schedule = new ScheduleRemediationEntry("s1", "S", true, "Blocked", "reason", false, false, DateTimeOffset.UtcNow.AddHours(1), "next");
        var controller = NewController(recovery: new StubRecoveryQueryService(blocked: [], schedule: schedule), recoveryGovernance: gov);
        var result = await controller.GetScheduleRemediation("s1", cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<ForbidResult>();
    }

    private static BlockedWorkRemediationEntry CreateBlockedEntry(string runId, string kind, bool retryEligibleNow) =>
        new(runId, "def-1", kind, "m1", DateTimeOffset.UtcNow.AddHours(-1),
            "blocked reason", retryEligibleNow, retryEligibleNow ? "eligible" : "not eligible");

    private static RecoveryController NewController(
        IRecoveryQueryService? recovery = null,
        IWorkflowRetryService? retry = null,
        IScheduleDispatchRunner? scheduleRunner = null,
        IRecoveryGovernanceService? recoveryGovernance = null,
        IPrincipalContext? principal = null,
        IAuditService? audit = null)
    {
        return new RecoveryController(
            recovery ?? new StubRecoveryQueryService(blocked: []),
            retry ?? new StubRetryService(),
            scheduleRunner ?? new StubScheduleDispatchRunner(),
            recoveryGovernance ?? new StubRecoveryGovernanceService(canRead: true, canManage: true),
            principal ?? new StubPrincipalContext(),
            audit);
    }

    private sealed class StubRecoveryGovernanceService : IRecoveryGovernanceService
    {
        private readonly bool _canRead;
        private readonly bool _canManage;

        public StubRecoveryGovernanceService(bool canRead = true, bool canManage = true)
        {
            _canRead = canRead;
            _canManage = canManage;
        }

        public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_canRead);

        public ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_canManage);
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

    private sealed class SpyAuditService : IAuditService
    {
        public Apperture.Core.Domain.Audit.AuditRecord? LastAppended { get; private set; }

        public ValueTask AppendAsync(Apperture.Core.Domain.Audit.AuditRecord record, CancellationToken cancellationToken = default)
        {
            LastAppended = record;
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));

        public ValueTask<Apperture.Core.Domain.Audit.AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Apperture.Core.Domain.Audit.AuditRecord?>(null);
    }
}
