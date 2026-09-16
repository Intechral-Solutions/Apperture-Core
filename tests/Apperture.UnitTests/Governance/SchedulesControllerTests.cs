using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;
using Apperture.Management.Api.Controllers;
using Apperture.UnitTests.ResourceContexts;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// Schedule capabilities API auth behavior.
/// </summary>
public sealed class SchedulesControllerTests
{
    [Fact]
    public async Task GetCapabilities_WhenCanManageFalse_Returns403()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: false);
        var scheduleManagement = new StubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation, PassthroughCreateTimeZoneDefaults.Instance, StubSchedulerStatusQueryService.Healthy, ResourceContextTestFactory.SharedOnly(), Mock.Of<IResourceOwnershipMoveService>());

        var result = await controller.GetCapabilities(CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetCapabilities_WhenCanManageTrue_Returns200WithCapabilities()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new StubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation, PassthroughCreateTimeZoneDefaults.Instance, StubSchedulerStatusQueryService.Healthy, ResourceContextTestFactory.SharedOnly(), Mock.Of<IResourceOwnershipMoveService>());

        var result = await controller.GetCapabilities(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var dto = (ScheduleCapabilitiesDto)ok.Value!;
        dto.CanManage.Should().BeTrue();
    }

    [Fact]
    public async Task List_WhenCanManage_Includes_NextFire_And_Status()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new ListCapableStubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(
            scheduleGovernance,
            scheduleManagement,
            validation,
            PassthroughCreateTimeZoneDefaults.Instance,
            StubSchedulerStatusQueryService.Healthy,
            ResourceContextTestFactory.SharedOnly(), Mock.Of<IResourceOwnershipMoveService>());

        var result = await controller.List(contextId: null, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var list = (IReadOnlyList<ScheduleDto>)ok.Value!;
        list.Should().ContainSingle();
        var dto = list[0];
        dto.NextFireUtc.Should().NotBeNull();
        dto.ScheduleTimeZoneId.Should().Be("UTC");
        dto.HealthStatus.Should().NotBeNullOrWhiteSpace();
        dto.StatusReasons.Should().NotBeNull();
        dto.StatusReasons!.Should().Contain(r => r.Code == "catch_up_policy");
    }

    [Fact]
    public async Task Get_WhenFound_Includes_NextFire_And_Status()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new DetailCapableStubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(
            scheduleGovernance,
            scheduleManagement,
            validation,
            PassthroughCreateTimeZoneDefaults.Instance,
            StubSchedulerStatusQueryService.Healthy,
            ResourceContextTestFactory.SharedOnly(), Mock.Of<IResourceOwnershipMoveService>());

        var result = await controller.Get("sched-detail-1", "local", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var dto = (ScheduleDetailDto)ok.Value!;
        dto.NextFireUtc.Should().NotBeNull();
        dto.ScheduleTimeZoneId.Should().Be("UTC");
        dto.HealthStatus.Should().NotBeNullOrWhiteSpace();
        dto.StatusReasons.Should().NotBeNull();
        dto.StatusReasons!.Should().Contain(r => r.Code == "not_due_yet" || r.Code == "catch_up_policy");
    }

    [Fact]
    public async Task Create_WithBlankTimeZone_UsesCreateDefaults()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new CreateCapableStubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var defaults = new CapturingCreateTimeZoneDefaults("America/Los_Angeles");
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation, defaults, StubSchedulerStatusQueryService.Healthy, ResourceContextTestFactory.SharedOnly(), Mock.Of<IResourceOwnershipMoveService>());
        var body = new CreateScheduleRequestBody(
            "Hourly sync",
            "0 * * * *",
            null,
            true,
            0,
            "{\"workflowDefinitionId\":\"Mod.Ns.Workflow1\"}",
            null,
            "local",
            IntegrationId: "integration-test-01");

        if (!ScheduleTimeZoneResolver.TryFind("America/Los_Angeles", out _))
            return;

        var result = await controller.Create(body, CancellationToken.None);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        defaults.LastRequested.Should().BeNull();
        var created = (CreatedAtActionResult)result.Result!;
        var dto = (ScheduleDto)created.Value!;
        dto.TimeZone.Should().Be("America/Los_Angeles");
    }

    [Fact]
    public async Task Create_WithValidWorkflowSchedule_Returns201()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new CreateCapableStubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation, PassthroughCreateTimeZoneDefaults.Instance, StubSchedulerStatusQueryService.Healthy, ResourceContextTestFactory.SharedOnly(), Mock.Of<IResourceOwnershipMoveService>());
        var body = new CreateScheduleRequestBody(
            "Hourly sync",
            "0 * * * *",
            "UTC",
            true,
            0,
            "{\"workflowDefinitionId\":\"Mod.Ns.Workflow1\"}",
            null,
            "local",
            IntegrationId: "integration-test-01");

        var result = await controller.Create(body, CancellationToken.None);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        var created = (CreatedAtActionResult)result.Result!;
        var dto = (ScheduleDto)created.Value!;
        dto.Name.Should().Be("Hourly sync");
        dto.RecurrenceExpression.Should().Be("0 * * * *");
        dto.TargetJobType.Should().Be(0);
    }

    [Fact]
    public async Task Create_WithInvalidCron_Returns400()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new StubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation, PassthroughCreateTimeZoneDefaults.Instance, StubSchedulerStatusQueryService.Healthy, ResourceContextTestFactory.SharedOnly(), Mock.Of<IResourceOwnershipMoveService>());
        var body = new CreateScheduleRequestBody(
            "Bad",
            "not valid cron",
            "UTC",
            true,
            0,
            "{\"workflowDefinitionId\":\"Mod.Ns.Workflow1\"}",
            null,
            "local",
            IntegrationId: "integration-test-01");

        var result = await controller.Create(body, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_WithoutScheduleManage_Returns403()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: false);
        var scheduleManagement = new StubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation, PassthroughCreateTimeZoneDefaults.Instance, StubSchedulerStatusQueryService.Healthy, ResourceContextTestFactory.SharedOnly(), Mock.Of<IResourceOwnershipMoveService>());
        var body = new CreateScheduleRequestBody("X", "0 * * * *", "UTC", true, 0, "{\"workflowDefinitionId\":\"wf1\"}", null, "local");

        var result = await controller.Create(body, CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Create_WithoutIntegrationId_Returns400()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new StubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation, PassthroughCreateTimeZoneDefaults.Instance, StubSchedulerStatusQueryService.Healthy, ResourceContextTestFactory.SharedOnly(), Mock.Of<IResourceOwnershipMoveService>());
        var body = new CreateScheduleRequestBody(
            "No integration",
            "0 * * * *",
            "UTC",
            true,
            0,
            "{\"workflowDefinitionId\":\"wf1\"}",
            null,
            "local",
            IntegrationId: null);

        var result = await controller.Create(body, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Update_WithInvalidCron_Returns400()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new UpdateCapableStubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation, PassthroughCreateTimeZoneDefaults.Instance, StubSchedulerStatusQueryService.Healthy, ResourceContextTestFactory.SharedOnly(), Mock.Of<IResourceOwnershipMoveService>());
        var body = new UpdateScheduleRequestBody(RecurrenceExpression: "invalid cron");

        var result = await controller.Update("s1", body, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Update_WithTargetJobType_Returns400_ImmutableTarget()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new UpdateCapableStubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation, PassthroughCreateTimeZoneDefaults.Instance, StubSchedulerStatusQueryService.Healthy, ResourceContextTestFactory.SharedOnly(), Mock.Of<IResourceOwnershipMoveService>());
        var body = new UpdateScheduleRequestBody(Name: "NewName", TargetJobType: 1);

        var result = await controller.Update("s1", body, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_WithTargetJobConfiguration_Returns400_ImmutableTarget()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new UpdateCapableStubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation, PassthroughCreateTimeZoneDefaults.Instance, StubSchedulerStatusQueryService.Healthy, ResourceContextTestFactory.SharedOnly(), Mock.Of<IResourceOwnershipMoveService>());
        var body = new UpdateScheduleRequestBody(Name: "NewName", TargetJobConfiguration: "{\"workflowDefinitionId\":\"other\"}");

        var result = await controller.Update("s1", body, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Update_WithoutScheduleManage_Returns403()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: false);
        var scheduleManagement = new UpdateCapableStubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation, PassthroughCreateTimeZoneDefaults.Instance, StubSchedulerStatusQueryService.Healthy, ResourceContextTestFactory.SharedOnly(), Mock.Of<IResourceOwnershipMoveService>());
        var body = new UpdateScheduleRequestBody(Name: "NewName");

        var result = await controller.Update("s1", body, CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    private sealed class ListCapableStubScheduleManagementService : IScheduleManagementService
    {
        public ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ScheduleDefinition?>(null);

        public ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default)
        {
            var def = new ScheduleDefinition(
                new ScheduleId("sched-list-1"),
                "Hourly",
                "0 * * * *",
                "UTC",
                true,
                new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{\"workflowDefinitionId\":\"wf1\"}"),
                null,
                1,
                new Apperture.Core.Domain.Integration.IntegrationId("integration-test-01"));
            return ValueTask.FromResult<IReadOnlyList<ScheduleDefinition>>([def]);
        }

        public ValueTask<ScheduleDefinition> CreateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask UpdateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ScheduleAssignment> AssignToRuntimePlaneAsync(ScheduleId scheduleId, RuntimePlaneId runtimePlaneId, string? policyOverrides, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask EnableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask DisableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId runtimePlaneId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ScheduleAssignment>>([]);

        public ValueTask<ScheduleWithExecutionState?> GetScheduleWithExecutionStateAsync(ScheduleId id, RuntimePlaneId planeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ScheduleWithExecutionState?>(null);
    }

    private sealed class DetailCapableStubScheduleManagementService : IScheduleManagementService
    {
        public ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ScheduleDefinition?>(null);

        public ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ScheduleDefinition>>([]);

        public ValueTask<ScheduleDefinition> CreateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask UpdateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ScheduleAssignment> AssignToRuntimePlaneAsync(ScheduleId scheduleId, RuntimePlaneId runtimePlaneId, string? policyOverrides, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask EnableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask DisableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId runtimePlaneId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ScheduleAssignment>>([]);

        public ValueTask<ScheduleWithExecutionState?> GetScheduleWithExecutionStateAsync(ScheduleId id, RuntimePlaneId planeId, CancellationToken cancellationToken = default)
        {
            var def = new ScheduleDefinition(
                id,
                "Daily",
                "0 12 * * *",
                "UTC",
                true,
                new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{\"workflowDefinitionId\":\"wf1\"}"),
                null,
                1,
                new Apperture.Core.Domain.Integration.IntegrationId("integration-test-01"));
            return ValueTask.FromResult<ScheduleWithExecutionState?>(
                new ScheduleWithExecutionState(def, ExecutionState: null, InProgressClaim: null));
        }
    }

    private sealed class CreateCapableStubScheduleManagementService : IScheduleManagementService
    {
        public ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ScheduleDefinition?>(null);

        public ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ScheduleDefinition>>([]);

        public ValueTask<ScheduleDefinition> CreateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(definition);

        public ValueTask UpdateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<ScheduleAssignment> AssignToRuntimePlaneAsync(ScheduleId scheduleId, RuntimePlaneId runtimePlaneId, string? policyOverrides, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ScheduleAssignment(new ScheduleAssignmentId("a1"), scheduleId, runtimePlaneId, policyOverrides, 1, 1));

        public ValueTask EnableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask DisableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId runtimePlaneId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ScheduleAssignment>>([]);

        public ValueTask<ScheduleWithExecutionState?> GetScheduleWithExecutionStateAsync(ScheduleId id, RuntimePlaneId planeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ScheduleWithExecutionState?>(null);
    }

    private sealed class UpdateCapableStubScheduleManagementService : IScheduleManagementService
    {
        private ScheduleDefinition? _definition;

        public ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_definition ?? new ScheduleDefinition(id, "Test", "0 * * * *", "UTC", true, new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{\"workflowDefinitionId\":\"wf1\"}"), null, 1));

        public ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ScheduleDefinition>>([]);

        public ValueTask<ScheduleDefinition> CreateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask UpdateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default)
        {
            _definition = definition;
            return ValueTask.CompletedTask;
        }

        public ValueTask<ScheduleAssignment> AssignToRuntimePlaneAsync(ScheduleId scheduleId, RuntimePlaneId runtimePlaneId, string? policyOverrides, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ScheduleAssignment(new ScheduleAssignmentId("a1"), scheduleId, runtimePlaneId, policyOverrides, 1, 1));

        public ValueTask EnableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask DisableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId runtimePlaneId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ScheduleAssignment>>([]);

        public ValueTask<ScheduleWithExecutionState?> GetScheduleWithExecutionStateAsync(ScheduleId id, RuntimePlaneId planeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ScheduleWithExecutionState?>(null);
    }

    private sealed class StubScheduleManagementService : IScheduleManagementService
    {
        public ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ScheduleDefinition?>(null);

        public ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ScheduleDefinition>>([]);

        public ValueTask<ScheduleDefinition> CreateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask UpdateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ScheduleAssignment> AssignToRuntimePlaneAsync(ScheduleId scheduleId, RuntimePlaneId runtimePlaneId, string? policyOverrides, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask EnableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisableAsync(ScheduleId scheduleId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId runtimePlaneId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ScheduleAssignment>>([]);

        public ValueTask<ScheduleWithExecutionState?> GetScheduleWithExecutionStateAsync(ScheduleId id, RuntimePlaneId planeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ScheduleWithExecutionState?>(null);
    }

    private sealed class StubScheduleGovernanceService : IScheduleGovernanceService
    {
        private readonly bool _canManage;

        public StubScheduleGovernanceService(bool canManage) => _canManage = canManage;

        public ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_canManage);
    }

    private sealed class PassthroughCreateTimeZoneDefaults : IScheduleCreateTimeZoneDefaults
    {
        public static readonly PassthroughCreateTimeZoneDefaults Instance = new();

        public ValueTask<string> ResolveCreateTimeZoneIdAsync(string? requestedTimeZone, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(string.IsNullOrWhiteSpace(requestedTimeZone) ? "UTC" : requestedTimeZone.Trim());
    }

    private sealed class CapturingCreateTimeZoneDefaults : IScheduleCreateTimeZoneDefaults
    {
        private readonly string _whenBlank;

        public CapturingCreateTimeZoneDefaults(string whenBlank) => _whenBlank = whenBlank;

        public string? LastRequested { get; private set; }

        public ValueTask<string> ResolveCreateTimeZoneIdAsync(string? requestedTimeZone, CancellationToken cancellationToken = default)
        {
            LastRequested = requestedTimeZone;
            return ValueTask.FromResult(string.IsNullOrWhiteSpace(requestedTimeZone) ? _whenBlank : requestedTimeZone.Trim());
        }
    }

    private sealed class StubSchedulerStatusQueryService : Apperture.Core.Application.RuntimeHealth.ISchedulerStatusQueryService
    {
        public static readonly StubSchedulerStatusQueryService Healthy = new("Healthy");

        private readonly string _status;

        public StubSchedulerStatusQueryService(string status) => _status = status;

        public ValueTask<Apperture.Core.Application.RuntimeHealth.SchedulerStatusSnapshot> GetStatusAsync(
            string? runtimePlaneId = null,
            CancellationToken cancellationToken = default)
        {
            var worker = new Apperture.Core.Application.RuntimeHealth.WorkerRuntimeStatus(
                InventoryModel: Apperture.Core.Application.RuntimeHealth.WorkerRuntimeInventoryModel.SingleHostObserved,
                Status: _status,
                HostId: "host-1",
                RuntimePlaneId: "local",
                LastSeenUtc: DateTimeOffset.UtcNow,
                Alignment: "Aligned",
                SchedulerStatus: _status,
                ProcessStartedUtc: null,
                ApplicationVersion: null,
                EnvironmentName: null,
                Guidance: null);
            return ValueTask.FromResult(new Apperture.Core.Application.RuntimeHealth.SchedulerStatusSnapshot(
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Status: _status,
                ExpectedManagementPlaneId: "local",
                ObservedRuntimePlaneId: "local",
                ObservedHostId: "host-1",
                Alignment: "Aligned",
                IsStale: false,
                PollIntervalSeconds: 30,
                LastCycleStartedUtc: null,
                LastCycleCompletedUtc: null,
                LastSuccessfulCycleUtc: null,
                UpdatedUtc: DateTimeOffset.UtcNow,
                AssignmentCount: null,
                DueCount: null,
                StaleCount: null,
                CandidateCount: null,
                ClaimedCount: null,
                DispatchedCount: null,
                SkippedCount: null,
                LastErrorSummary: null,
                Guidance: null,
                WorkerRuntime: worker));
        }
    }
}
