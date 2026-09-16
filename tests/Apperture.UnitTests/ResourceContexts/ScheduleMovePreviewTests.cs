using Apperture.Core.Application.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

public sealed class ScheduleMovePreviewTests
{
    [Fact]
    public async Task Attached_schedule_blocks_with_link_to_integration()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);

        var preview = await h.Service.PreviewScheduleMoveAsync(schedule.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        var blocker = preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.ScheduleAttachedToIntegration).Subject;
        blocker.RelatedResourceType.Should().Be(ResourceMoveResourceTypes.Integration);
        blocker.RelatedResourceId.Should().Be(integration.Id.Value);
    }

    [Fact]
    public async Task Standalone_schedule_moves()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var preview = await h.Service.PreviewScheduleMoveAsync(schedule.Id, target.Id);

        preview.CanMove.Should().BeTrue();
    }

    [Fact]
    public async Task Not_authorized_throws()
    {
        await using var h = new ResourceOwnershipMoveTestHarness(grantedAction: null);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var act = async () => await h.Service.PreviewScheduleMoveAsync(schedule.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Schedule_manage_permission_is_required_not_resource_context_manage()
    {
        await using var h = new ResourceOwnershipMoveTestHarness(
            grantedAction: Apperture.Core.Domain.Governance.ActionConstants.ResourceContextManage,
            grantedResourceType: Apperture.Core.Domain.Governance.ResourceTypeConstants.ResourceContext);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var act = async () => await h.Service.PreviewScheduleMoveAsync(schedule.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "holding resource_context.manage must not be enough -- schedule.manage is the required permission (§9.3)");
    }

    [Fact]
    public async Task Schedule_manage_permission_is_sufficient()
    {
        // Positive counterpart to Schedule_manage_permission_is_required_not_resource_context_manage:
        // every other test in this file builds the harness with the default grantedAction: "any", which
        // short-circuits permission matching entirely. This pins the ACTUAL action/resource-type the
        // service requests -- without it, substituting any other action constant in the implementation
        // would leave every other test green.
        await using var h = new ResourceOwnershipMoveTestHarness(
            grantedAction: Apperture.Core.Domain.Governance.ActionConstants.ScheduleManage,
            grantedResourceType: Apperture.Core.Domain.Governance.ResourceTypeConstants.Schedule);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var preview = await h.Service.PreviewScheduleMoveAsync(schedule.Id, target.Id);

        preview.CanMove.Should().BeTrue();
    }
}
