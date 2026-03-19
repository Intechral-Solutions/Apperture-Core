using Apperture.ControlPlane.Security;
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
        var controller = new SchedulesController(scheduleGovernance);

        var result = await controller.GetCapabilities(CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetCapabilities_WhenCanManageTrue_Returns200WithCapabilities()
    {
        var scheduleGovernance = new StubScheduleGovernanceService(canManage: true);
        var controller = new SchedulesController(scheduleGovernance);

        var result = await controller.GetCapabilities(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var dto = (ScheduleCapabilitiesDto)ok.Value!;
        dto.CanManage.Should().BeTrue();
    }

    private sealed class StubScheduleGovernanceService : IScheduleGovernanceService
    {
        private readonly bool _canManage;

        public StubScheduleGovernanceService(bool canManage) => _canManage = canManage;

        public ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_canManage);
    }
}
