using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

public sealed class ResourceOwnershipMoveMutationTests
{
    [Fact]
    public async Task Successful_integration_move_updates_current_ownership_and_moves_attached_schedules()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);

        var result = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        result.CanMove.Should().BeTrue();
        (await h.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value.Should().Be(target.Id.Value);
        (await h.ScheduleRepository.GetDefinitionAsync(schedule.Id))!.ResourceContextId!.Value.Should().Be(target.Id.Value);
    }

    [Fact]
    public async Task Blocked_integration_move_leaves_the_integration_and_its_schedules_unchanged()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);
        var connection = await h.CreateConnectionAsync(thirdParty.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        var result = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        result.CanMove.Should().BeFalse();
        (await h.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value.Should().Be(source.Id.Value);
        (await h.ScheduleRepository.GetDefinitionAsync(schedule.Id))!.ResourceContextId!.Value.Should().Be(source.Id.Value,
            "atomicity cuts both ways -- a blocked move must not move the attached schedules either");
    }

    [Fact]
    public async Task Successful_connection_move_updates_current_ownership()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        var result = await h.Service.MoveConnectionAsync(connection.Id, target.Id);

        result.CanMove.Should().BeTrue();
        (await h.ConnectionRepository.GetByIdAsync(connection.Id))!.ResourceContextId.Value.Should().Be(target.Id.Value);
    }

    [Fact]
    public async Task Blocked_connection_move_mutates_nothing()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var connection = await h.CreateConnectionAsync(source.Id);
        var boundIntegration = await h.CreateIntegrationAsync(thirdParty.Id);
        await h.AddDirectBindingAsync(boundIntegration.Id, boundIntegration.ModuleId, connection.Id);

        var result = await h.Service.MoveConnectionAsync(connection.Id, target.Id);

        result.CanMove.Should().BeFalse();
        (await h.ConnectionRepository.GetByIdAsync(connection.Id))!.ResourceContextId.Value.Should().Be(source.Id.Value);
    }

    [Fact]
    public async Task Standalone_schedule_move_updates_current_ownership()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var result = await h.Service.MoveScheduleAsync(schedule.Id, target.Id);

        result.CanMove.Should().BeTrue();
        (await h.ScheduleRepository.GetDefinitionAsync(schedule.Id))!.ResourceContextId!.Value.Should().Be(target.Id.Value);
    }

    [Fact]
    public async Task Attached_schedule_move_is_rejected_and_mutates_nothing()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);

        var result = await h.Service.MoveScheduleAsync(schedule.Id, target.Id);

        result.CanMove.Should().BeFalse();
        (await h.ScheduleRepository.GetDefinitionAsync(schedule.Id))!.ResourceContextId!.Value.Should().Be(source.Id.Value);
    }

    [Fact]
    public async Task Moving_out_of_an_archived_source_succeeds()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id, IntegrationState.Retired);
        await h.ArchiveAsync(source.Id);

        var result = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        result.CanMove.Should().BeTrue("moving out of an archived Application is legitimate cleanup");
        (await h.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value.Should().Be(target.Id.Value);
    }

    [Fact]
    public async Task Not_authorized_move_throws_and_mutates_nothing()
    {
        await using var h = new ResourceOwnershipMoveTestHarness(grantedAction: null);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        var act = async () => await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        (await h.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value.Should().Be(source.Id.Value);
    }

    [Fact]
    public async Task Move_requires_the_family_permission_not_resource_context_manage()
    {
        await using var h = new ResourceOwnershipMoveTestHarness(
            grantedAction: Apperture.Core.Domain.Governance.ActionConstants.ResourceContextManage,
            grantedResourceType: Apperture.Core.Domain.Governance.ResourceTypeConstants.ResourceContext);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var connection = await h.CreateConnectionAsync(source.Id);
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var moveIntegration = async () => await h.Service.MoveIntegrationAsync(integration.Id, target.Id);
        var moveConnection = async () => await h.Service.MoveConnectionAsync(connection.Id, target.Id);
        var moveSchedule = async () => await h.Service.MoveScheduleAsync(schedule.Id, target.Id);

        await moveIntegration.Should().ThrowAsync<UnauthorizedAccessException>();
        await moveConnection.Should().ThrowAsync<UnauthorizedAccessException>();
        await moveSchedule.Should().ThrowAsync<UnauthorizedAccessException>(
            "the mutating path enforces the same family permissions as preview (§9.3), not resource_context.manage");
    }
}
