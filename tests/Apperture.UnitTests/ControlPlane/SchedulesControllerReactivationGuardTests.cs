using Apperture.ControlPlane.Security;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// CTX-3A carried-forward fix (Plan 1 final review): <see cref="SchedulesController.Update"/> had no
/// exception handler at all, so a <see cref="ResourceContextArchivedException"/> thrown by the Plan-1
/// update-path reactivation guard (enabling a schedule owned by an archived Application) surfaced as an
/// unhandled 500. It must now map to 409, and a plain <see cref="InvalidOperationException"/> to 400 --
/// with the archived-context catch ordered before the plain one, since it is a subtype.
/// </summary>
public sealed class SchedulesControllerReactivationGuardTests
{
    private static ScheduleDefinition ExistingDefinition() => new(
        new ScheduleId("s1"),
        "Nightly",
        "0 0 * * *",
        "UTC",
        Enabled: false,
        new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
        null,
        Version: 1);

    private static SchedulesController NewController(Mock<IScheduleManagementService> scheduleManagement)
    {
        scheduleManagement
            .Setup(s => s.GetDefinitionAsync(It.IsAny<ScheduleId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingDefinition());

        var scheduleGovernance = new Mock<IScheduleGovernanceService>();
        scheduleGovernance.Setup(g => g.CanManageAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var validation = new Mock<IScheduleValidationService>();
        validation
            .Setup(v => v.ValidateUpdate(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>()))
            .Returns(ScheduleValidationResult.Ok());

        return new SchedulesController(
            scheduleGovernance.Object,
            scheduleManagement.Object,
            validation.Object,
            Mock.Of<IScheduleCreateTimeZoneDefaults>(),
            Mock.Of<ISchedulerStatusQueryService>(),
            Mock.Of<IResourceContextService>());
    }

    [Fact]
    public async Task Update_WhenReactivationGuardThrowsResourceContextArchived_Returns409WithMessage()
    {
        var scheduleManagement = new Mock<IScheduleManagementService>();
        scheduleManagement
            .Setup(s => s.UpdateScheduleAsync(It.IsAny<ScheduleDefinition>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceContextArchivedException(
                new ResourceContextId("app-a"),
                "Canvas",
                "ResourceContext 'Canvas' (app-a) is archived and cannot own new resources."));
        var controller = NewController(scheduleManagement);

        var result = await controller.Update("s1", new UpdateScheduleRequestBody(Enabled: true), CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeEquivalentTo(new { error = "ResourceContext 'Canvas' (app-a) is archived and cannot own new resources." });
    }

    [Fact]
    public async Task Update_WhenPlainInvalidOperationException_Returns400()
    {
        var scheduleManagement = new Mock<IScheduleManagementService>();
        scheduleManagement
            .Setup(s => s.UpdateScheduleAsync(It.IsAny<ScheduleDefinition>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Something else went wrong."));
        var controller = NewController(scheduleManagement);

        var result = await controller.Update("s1", new UpdateScheduleRequestBody(Enabled: true), CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { error = "Something else went wrong." });
    }
}
