using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Scheduling;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
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
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation);

        var result = await controller.GetCapabilities(CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetCapabilities_WhenCanManageTrue_Returns200WithCapabilities()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new StubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation);

        var result = await controller.GetCapabilities(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var dto = (ScheduleCapabilitiesDto)ok.Value!;
        dto.CanManage.Should().BeTrue();
    }

    [Fact]
    public async Task Create_WithValidWorkflowSchedule_Returns201()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new CreateCapableStubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation);
        var body = new CreateScheduleRequestBody(
            "Hourly sync",
            "0 * * * *",
            "UTC",
            true,
            0,
            "{\"workflowDefinitionId\":\"Mod.Ns.Workflow1\"}",
            null,
            "local");

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
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation);
        var body = new CreateScheduleRequestBody(
            "Bad",
            "not valid cron",
            "UTC",
            true,
            0,
            "{\"workflowDefinitionId\":\"Mod.Ns.Workflow1\"}",
            null,
            "local");

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
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation);
        var body = new CreateScheduleRequestBody("X", "0 * * * *", "UTC", true, 0, "{\"workflowDefinitionId\":\"wf1\"}", null, "local");

        var result = await controller.Create(body, CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Update_WithInvalidCron_Returns400()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new UpdateCapableStubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement, validation);
        var body = new UpdateScheduleRequestBody(RecurrenceExpression: "invalid cron");

        var result = await controller.Update("s1", body, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
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
}
