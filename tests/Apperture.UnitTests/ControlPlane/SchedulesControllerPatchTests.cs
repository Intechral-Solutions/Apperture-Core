using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.RuntimeHealth;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Scheduling;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// AUD-1.4 slice 4 remediation of review finding M1: <see cref="SchedulesController.Update"/> reads nothing before the service,
/// hands it exactly the fields the body carries, and answers with the schedule the service committed.
/// </summary>
public sealed class SchedulesControllerPatchTests
{
    private static ScheduleDefinition Committed() => new(
        new ScheduleId("s1"),
        "Renamed",
        "0 0 * * *",
        "UTC",
        Enabled: false,
        new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
        null,
        Version: 1);

    [Fact]
    public async Task The_body_becomes_a_patch_with_omitted_fields_left_out_and_nothing_is_read_first()
    {
        var schedules = new Mock<IScheduleManagementService>(MockBehavior.Strict);
        SchedulePatch? sent = null;
        schedules
            .Setup(s => s.PatchScheduleAsync(new ScheduleId("s1"), It.IsAny<SchedulePatch>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduleId, SchedulePatch, CancellationToken>((_, patch, _) => sent = patch)
            .ReturnsAsync(Committed());

        var result = await NewController(schedules.Object).Update("s1", new UpdateScheduleRequestBody(Name: "Renamed"), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        sent.Should().Be(new SchedulePatch(Name: "Renamed"), "the omitted recurrence, time zone and enabled flag are not sent");
        schedules.Verify(s => s.GetDefinitionAsync(It.IsAny<ScheduleId>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task The_response_is_the_committed_schedule()
    {
        var schedules = new Mock<IScheduleManagementService>();
        // The service committed over a disable that landed first: the response must say disabled, not the body's view.
        schedules
            .Setup(s => s.PatchScheduleAsync(It.IsAny<ScheduleId>(), It.IsAny<SchedulePatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Committed());

        var result = await NewController(schedules.Object).Update("s1", new UpdateScheduleRequestBody(Name: "Renamed"), CancellationToken.None);

        var dto = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ScheduleDto>().Subject;
        dto.Name.Should().Be("Renamed");
        dto.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_schedule_is_404()
    {
        var schedules = new Mock<IScheduleManagementService>();
        schedules
            .Setup(s => s.PatchScheduleAsync(It.IsAny<ScheduleId>(), It.IsAny<SchedulePatch>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ScheduleNotFoundException(new ScheduleId("s1")));

        var result = await NewController(schedules.Object).Update("s1", new UpdateScheduleRequestBody(Name: "Renamed"), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task An_audit_invariant_is_not_mapped_to_a_refusal()
    {
        var failure = new AuditInvariantException("The audited operation expected a 'schedule.updated' row and staged none.");
        var schedules = new Mock<IScheduleManagementService>();
        schedules
            .Setup(s => s.PatchScheduleAsync(It.IsAny<ScheduleId>(), It.IsAny<SchedulePatch>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var act = () => NewController(schedules.Object).Update("s1", new UpdateScheduleRequestBody(Name: "Renamed"), CancellationToken.None);

        (await act.Should().ThrowAsync<AuditInvariantException>()).Which.Should().BeSameAs(failure);
    }

    private static SchedulesController NewController(IScheduleManagementService schedules)
    {
        var governance = new Mock<IScheduleGovernanceService>();
        governance.Setup(g => g.CanManageAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var validation = new Mock<IScheduleValidationService>();
        validation
            .Setup(v => v.ValidateUpdate(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>()))
            .Returns(ScheduleValidationResult.Ok());

        return new SchedulesController(
            governance.Object,
            schedules,
            validation.Object,
            Mock.Of<IScheduleCreateTimeZoneDefaults>(),
            Mock.Of<ISchedulerStatusQueryService>(),
            Mock.Of<IResourceContextService>(),
            Mock.Of<IResourceOwnershipMoveService>());
    }
}
