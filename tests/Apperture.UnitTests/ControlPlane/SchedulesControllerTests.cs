using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// CTX-2 completion: <see cref="SchedulesController"/>'s <c>ScheduleDetailDto</c> mapping, focused on the
/// distinction between the Schedule's current ownership (CTX-1 <c>ResourceContextId</c>) and the historical
/// in-progress-claim snapshot (CTX-2 <c>ClaimResourceContextId</c>).
/// </summary>
public sealed class SchedulesControllerTests
{
    [Fact]
    public async Task Get_ExposesClaimResourceContextId_WhenAnInProgressClaimExists()
    {
        var definition = new ScheduleDefinition(
            new ScheduleId("s1"),
            "Nightly",
            "0 0 * * *",
            "UTC",
            true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            null,
            Version: 1)
        {
            ResourceContextId = new ResourceContextId("shared")
        };
        var claim = new ScheduleInProgressClaim("host-1", DateTimeOffset.UtcNow, IsStale: false, ResourceContextId: "app-a");
        var detail = new ScheduleWithExecutionState(definition, null, claim);

        var scheduleManagement = new Mock<IScheduleManagementService>();
        scheduleManagement
            .Setup(s => s.GetScheduleWithExecutionStateAsync(It.IsAny<ScheduleId>(), It.IsAny<RuntimePlaneId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var scheduleGovernance = new Mock<IScheduleGovernanceService>();
        scheduleGovernance.Setup(g => g.CanManageAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var resourceContexts = new Mock<IResourceContextService>();
        resourceContexts
            .Setup(r => r.GetAsync(It.IsAny<ResourceContextId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceContextId id, CancellationToken _) =>
                new ResourceContext(id, id.Value, id.Value, ResourceContextKinds.Application, ResourceContextStates.Active, false, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        var controller = new SchedulesController(
            scheduleGovernance.Object,
            scheduleManagement.Object,
            Mock.Of<IScheduleValidationService>(),
            Mock.Of<IScheduleCreateTimeZoneDefaults>(),
            Mock.Of<ISchedulerStatusQueryService>(),
            resourceContexts.Object,
            Mock.Of<IResourceOwnershipMoveService>());

        var result = await controller.Get("s1", runtimePlaneId: "local", CancellationToken.None);

        var dto = (ScheduleDetailDto)result.Result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        dto.ClaimResourceContextId.Should().Be("app-a");
        dto.ResourceContextId.Should().Be("shared", "the Schedule's current ownership (CTX-1) must remain distinct from the claim's historical snapshot (CTX-2)");
    }
}
