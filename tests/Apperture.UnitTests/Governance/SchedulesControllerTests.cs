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
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement);

        var result = await controller.GetCapabilities(CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetCapabilities_WhenCanManageTrue_Returns200WithCapabilities()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var scheduleManagement = new StubScheduleManagementService();
        var controller = new SchedulesController(scheduleGovernance, scheduleManagement);

        var result = await controller.GetCapabilities(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var dto = (ScheduleCapabilitiesDto)ok.Value!;
        dto.CanManage.Should().BeTrue();
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
