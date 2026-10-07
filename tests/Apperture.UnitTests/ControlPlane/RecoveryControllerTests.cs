using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Services;
using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Environment;
using Apperture.Management.Api.Controllers;
using Apperture.RuntimePlane.Abstractions;
using Apperture.UnitTests.Audit;
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
        message.Should().Contain("Affected records", "response must guide operators to item-level recovery");
        message.Should().Contain("not offered", "response must state whole-operation retry is intentionally unsupported");
        var finalDecision = value.GetType().GetProperty("finalDecision")?.GetValue(value);
        finalDecision.Should().Be(true);
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
        var accepted = result.Result.Should().BeOfType<AcceptedResult>().Subject;
        runner.RunCycleCalled.Should().BeTrue("controller must invoke IScheduleDispatchRunner.RunCycleAsync");
        var body = accepted.Value.Should().BeOfType<SchedulerManualOperationResult>().Subject;
        body.Action.Should().Be(SchedulerManualOperationMapper.ActionRunCycle);
        body.WritesHeartbeat.Should().BeFalse();
        body.Message.Should().Contain("not schedule-specific", "response must clarify this is global re-evaluation, not schedule-specific force-fire");
        body.Message.Should().Contain("heartbeat", "SWM-8: manual cycle must disclose it does not write heartbeat");
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
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task RunScheduleCycle_WithRecoveryManage_Returns202()
    {
        var gov = new StubRecoveryGovernanceService(canRead: true, canManage: true);
        var runner = new StubScheduleDispatchRunner();
        var controller = NewController(recoveryGovernance: gov, scheduleRunner: runner);
        var result = await controller.RunScheduleCycle(cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<AcceptedResult>();
        runner.RunCycleCalled.Should().BeTrue();
    }

    [Fact]
    public async Task RetryBlockedWork_WhenDenied_AuditsRetryDeniedAsTheCaller()
    {
        var entry = CreateBlockedEntry("r1", "workflow", true);
        var gov = new StubRecoveryGovernanceService(canRead: true, canManage: false);
        var events = new RecordingAuditEventWriter();
        var controller = NewController(
            recovery: new StubRecoveryQueryService(blocked: [entry], single: entry),
            recoveryGovernance: gov,
            events: events);
        var result = await controller.RetryBlockedWork("r1", CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();

        var (denied, actor, bestEffort) = events.Written.Should().ContainSingle("denial audit policy requires recovery retry denials to be audited").Which;
        denied.Action.Should().Be(AuditActionNames.RecoveryRetryDenied);
        denied.OutcomeCode.Should().Be(AuditOutcomeCodes.Denied);
        denied.Fields["runId"].Should().Be("r1");
        actor.IsCurrentPrincipal.Should().BeTrue("the denial is the signed-in caller's, not the platform's");
        bestEffort.Should().BeTrue();
    }

    [Fact]
    public async Task RetryBlockedWork_WhenDeniedForACallerMappedToNoSession_IsRecordedAsNotSignedInNeverAsSystem()
    {
        var gov = new StubRecoveryGovernanceService(canRead: true, canManage: false);
        var events = new RecordingAuditEventWriter();
        var controller = NewController(recoveryGovernance: gov, principal: new StubPrincipalContext(signedIn: false), events: events);

        await controller.RetryBlockedWork("r1", CancellationToken.None);

        var (_, actor, _) = events.Written.Should().ContainSingle().Which;
        actor.Should().Be(AuditActor.Anonymous);
        actor.RecordedValue.Should().NotBe("system").And.NotBe("system:unknown");
    }

    [Fact]
    public async Task RetryBlockedWork_WhenDeniedWithAnOverlongRunIdText_StillRecordsTheDenialWithoutIt()
    {
        var gov = new StubRecoveryGovernanceService(canRead: true, canManage: false);
        var events = new RecordingAuditEventWriter();
        var controller = NewController(recoveryGovernance: gov, events: events);

        var result = await controller.RetryBlockedWork(new string('x', 4000), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        var denied = events.Written.Should().ContainSingle().Which.Event;
        denied.Fields.Should().NotContainKey("runId", "the caller's own text is recorded only when the schema can hold it");
        denied.Related.RunId.Should().BeNull();
    }

    [Fact]
    public async Task RetryBlockedWork_WhenNotEligible_RecordsTheRefusalAgainstTheBlockedRunAndItsOwnDefinition()
    {
        var entry = CreateBlockedEntry("r1", "workflow", retryEligibleNow: false);
        var events = new RecordingAuditEventWriter();
        var controller = NewController(recovery: new StubRecoveryQueryService(blocked: [entry], single: entry), events: events);

        await controller.RetryBlockedWork("r1", CancellationToken.None);

        var refusal = events.Single(AuditActionNames.BlockedWorkRetryRefused);
        refusal.Target!.Id.Should().Be("r1");
        refusal.Fields["definitionId"].Should().Be("def-1");
        refusal.Fields["reason"].Should().Be("module_not_executable");
        refusal.OutcomeCode.Should().Be(AuditOutcomeCodes.Rejected);
    }

    [Fact]
    public async Task RetryBlockedWork_WhenTheRetryItselfIsBlocked_RecordsTheDefinitionNotTheRunIdInItsPlace()
    {
        // The earlier code passed the original RUN id where the DEFINITION id belongs on this branch.
        var entry = CreateBlockedEntry("r1", "workflow", true);
        var blockedResult = new WorkflowRetryResult(
            new OperationRunId("r1"), null, null, new WorkflowDefinitionId("wf-from-result"), "blocked",
            IsBlocked: true, BlockedReason: "Module disabled");
        var events = new RecordingAuditEventWriter();
        var controller = NewController(
            recovery: new StubRecoveryQueryService(blocked: [entry], single: entry),
            retry: new StubRetryService(blockedResult),
            events: events);

        var result = await controller.RetryBlockedWork("r1", CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
        var refusal = events.Single(AuditActionNames.BlockedWorkRetryRefused);
        refusal.Fields["runId"].Should().Be("r1");
        refusal.Fields["definitionId"].Should().Be("def-1").And.NotBe("r1");
        refusal.Fields["reason"].Should().Be("definition_not_executable");
    }

    [Fact]
    public async Task RetryBlockedWork_OnSuccess_AsksForTheRecoveryChannelAndWritesNoRowOfItsOwn()
    {
        // The committed retry records itself (naming the new run) in the same unit as the retry, under the recovery action.
        // The controller no longer writes a "requested" row before the work or a "dispatched" row after it.
        var entry = CreateBlockedEntry("r1", "workflow", true);
        var retry = new StubRetryService();
        var events = new RecordingAuditEventWriter();
        var controller = NewController(recovery: new StubRecoveryQueryService(blocked: [entry], single: entry), retry: retry, events: events);

        var result = await controller.RetryBlockedWork("r1", CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
        retry.Channels.Should().Equal(WorkflowRetryChannel.BlockedWork);
        events.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task RunScheduleCycle_WhenDenied_AuditsRunCycleDenied()
    {
        var gov = new StubRecoveryGovernanceService(canRead: true, canManage: false);
        var events = new RecordingAuditEventWriter();
        var controller = NewController(recoveryGovernance: gov, events: events);
        var result = await controller.RunScheduleCycle(cancellationToken: CancellationToken.None);
        result.Result.Should().BeOfType<ForbidResult>();
        var denied = events.Single(AuditActionNames.SchedulerOperationDenied);
        denied.Fields["operation"].Should().Be("run_cycle");
        denied.OutcomeCode.Should().Be(AuditOutcomeCodes.Denied);
    }

    [Fact]
    public async Task RunScheduleCycle_RecordsWhatTheCycleReported()
    {
        var events = new RecordingAuditEventWriter();
        var controller = NewController(events: events);

        await controller.RunScheduleCycle("plane-a", CancellationToken.None);

        var recorded = events.Single(AuditActionNames.SchedulerOperationRequested);
        recorded.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        recorded.Fields["runtimePlaneId"].Should().Be("plane-a");
        recorded.Fields["operation"].Should().Be("run_cycle");
    }

    [Fact]
    public async Task RunScheduleCycle_WhenTheCycleThrows_IsRecordedAsFailedNotAsCompleted()
    {
        var events = new RecordingAuditEventWriter();
        var controller = NewController(scheduleRunner: new ThrowingScheduleDispatchRunner(), events: events);

        var result = await controller.RunScheduleCycle(cancellationToken: CancellationToken.None);

        result.Result.Should().BeOfType<AcceptedResult>("the operator still gets the sanitized report");
        events.Single(AuditActionNames.SchedulerOperationRequested).OutcomeCode.Should().Be(AuditOutcomeCodes.Failed);
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
        IDevWorkerRuntimeRecoveryService? devWorkerRecovery = null,
        IRecoveryGovernanceService? recoveryGovernance = null,
        IPrincipalContext? principal = null,
        RecordingAuditEventWriter? events = null)
    {
        return new RecoveryController(
            recovery ?? new StubRecoveryQueryService(blocked: []),
            retry ?? new StubRetryService(),
            scheduleRunner ?? new StubScheduleDispatchRunner(),
            devWorkerRecovery ?? new StubDevWorkerRuntimeRecoveryService(),
            recoveryGovernance ?? new StubRecoveryGovernanceService(canRead: true, canManage: true),
            principal ?? new StubPrincipalContext(),
            events ?? new RecordingAuditEventWriter());
    }

    private sealed class StubDevWorkerRuntimeRecoveryService : IDevWorkerRuntimeRecoveryService
    {
        public ValueTask<DevWorkerRuntimeRecoveryResult> StartAsync(bool restart, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DevWorkerRuntimeRecoveryResult(true, "ok", 0, null, null));

        public ValueTask<DevWorkerRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DevWorkerRuntimeStatus("Running", "ok", 123, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));
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

        public List<WorkflowRetryChannel> Channels { get; } = [];

        public ValueTask<WorkflowRetryResult?> RequestRetryAsync(OperationRunId runId, WorkflowRetryChannel channel, CancellationToken cancellationToken = default)
        {
            Channels.Add(channel);
            return ValueTask.FromResult<WorkflowRetryResult?>(_result ?? new WorkflowRetryResult(
                runId, new OperationRunId("new"), new WorkflowInstanceId("inst"), new WorkflowDefinitionId("wf"), "requested"));
        }
    }

    private sealed class ThrowingScheduleDispatchRunner : IScheduleDispatchRunner
    {
        public ValueTask<ScheduleDispatchCycleResult> RunCycleAsync(RuntimePlaneId planeId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the cycle failed");

        public ValueTask<ScheduleDispatchCycleResult> RecoverStaleAsync(RuntimePlaneId planeId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the cycle failed");
    }

    private sealed class StubScheduleDispatchRunner : IScheduleDispatchRunner
    {
        public bool RunCycleCalled { get; private set; }
        public bool RecoverStaleCalled { get; private set; }

        public ValueTask<ScheduleDispatchCycleResult> RunCycleAsync(RuntimePlaneId planeId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default)
        {
            RunCycleCalled = true;
            return ValueTask.FromResult(new ScheduleDispatchCycleResult(0, 0, 0, AssignmentCount: 1, HostId: "mgmt-host"));
        }

        public ValueTask<ScheduleDispatchCycleResult> RecoverStaleAsync(RuntimePlaneId planeId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default)
        {
            RecoverStaleCalled = true;
            return ValueTask.FromResult(new ScheduleDispatchCycleResult(0, 0, 0, DueCount: 0, StaleCount: 0, HostId: "mgmt-host"));
        }
    }

    private sealed class StubPrincipalContext(bool signedIn = true) : IPrincipalContext
    {
        public ValueTask<Apperture.Shared.Kernel.IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Apperture.Shared.Kernel.IActor?>(
                signedIn ? new HumanActor(new Apperture.Shared.Kernel.ActorId("usr_recovery_operator"), "operator") : null);
    }
}
