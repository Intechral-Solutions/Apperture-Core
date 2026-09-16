using Apperture.Core.Application.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

public sealed class ScheduleMovePreviewTests
{
    // --- Part B1: common blockers, own copies for the Schedule family (same reasoning as the Connection
    // family's A1: ValidateScheduleMoveAsync has its own copies of the three common branches). Every test
    // here uses a standalone schedule -- an attached schedule short-circuits to
    // schedule_attached_to_integration before the target is ever examined (deliberate, §8.6; see
    // Attached_schedule_precedes_target_checks_even_for_a_nonexistent_target below). ---

    [Fact]
    public async Task Standalone_schedule_target_not_found_is_a_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var preview = await h.Service.PreviewScheduleMoveAsync(schedule.Id, new(Guid.NewGuid().ToString("N")));

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.TargetNotFound);
    }

    [Fact]
    public async Task Standalone_schedule_target_archived_is_a_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);
        await h.ArchiveAsync(target.Id);

        var preview = await h.Service.PreviewScheduleMoveAsync(schedule.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.TargetArchived);
    }

    [Fact]
    public async Task Standalone_schedule_same_target_is_a_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var preview = await h.Service.PreviewScheduleMoveAsync(schedule.Id, source.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.AlreadyInTarget);
    }

    // --- Part B (precedence): the attachment check runs BEFORE the target checks, deliberately (§8.6) --
    // this documents that ordering explicitly so it reads as intended behavior, not an oversight to "fix". ---

    [Fact]
    public async Task Attached_schedule_precedes_target_checks_even_for_a_nonexistent_target()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);

        var preview = await h.Service.PreviewScheduleMoveAsync(schedule.Id, new(Guid.NewGuid().ToString("N")));

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.ScheduleAttachedToIntegration,
            "an Integration-attached Schedule cannot move independently at all (§8.6) -- its preview reports only the attachment blocker, even when the requested target does not exist, because the operator's actionable next step is always \"move the integration instead\", never \"also fix the target\"");
        preview.Blockers.Should().NotContain(b => b.Code == ResourceMoveBlockerCodes.TargetNotFound);
    }

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
        preview.ResourceType.Should().Be(ResourceMoveResourceTypes.Schedule);
        var blocker = preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.ScheduleAttachedToIntegration).Subject;
        blocker.RelatedResourceType.Should().Be(ResourceMoveResourceTypes.Integration);
        blocker.RelatedResourceId.Should().Be(integration.Id.Value);
        // Fix round 1, Important 1: an attached Schedule's preview still fetches and reports the real
        // target ref -- the attachment blocker fires regardless, but preview.Target is part of the
        // API/UI contract and an operator must see the actual destination, not the target's raw id.
        preview.Target.Id.Should().Be(target.Id.Value);
        preview.Target.Name.Should().Be("Canvas");
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
        preview.ResourceType.Should().Be(ResourceMoveResourceTypes.Schedule);
        preview.Blockers.Should().BeEmpty();
        preview.Source.Id.Should().Be(source.Id.Value);
        preview.Target.Id.Should().Be(target.Id.Value);
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
