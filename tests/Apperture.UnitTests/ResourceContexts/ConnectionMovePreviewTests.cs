using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

public sealed class ConnectionMovePreviewTests
{
    [Fact]
    public async Task No_bindings_moves_cleanly()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeTrue();
    }

    [Fact]
    public async Task All_bindings_compatible_moves_cleanly_and_shows_used_by()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        // Put the bound Integration in the target first, so the Connection's move into the target leaves
        // every reference valid. (Fixture-only rewrite; Plan 2 replaces this with the real move.)
        await h.MoveIntegrationDirectlyForFixtureAsync(integration.Id, target.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Blockers.Should().BeEmpty();
        var usedBy = preview.Effects.Should().ContainSingle(e => e.Code == ResourceMoveEffectCodes.UsedByIntegration).Subject;
        usedBy.RelatedResourceIds.Should().ContainSingle().Which.Should().Be(integration.Id.Value,
            "blast radius is shown even when CanMove is true (§8.5 confirmation UX)");
    }

    [Fact]
    public async Task One_incompatible_integration_blocks()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var connection = await h.CreateConnectionAsync(source.Id);
        var boundIntegration = await h.CreateIntegrationAsync(thirdParty.Id); // not source, not target
        await h.AddDirectBindingAsync(boundIntegration.Id, boundIntegration.ModuleId, connection.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        var blocker = preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingIntegrationUnreachable).Subject;
        blocker.RelatedResourceId.Should().Be(boundIntegration.Id.Value);
    }

    [Fact]
    public async Task Inactive_and_retired_integration_bindings_still_count()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var connection = await h.CreateConnectionAsync(source.Id);
        var retiredIntegration = await h.CreateIntegrationAsync(thirdParty.Id, IntegrationState.Retired);
        await h.AddDirectBindingAsync(retiredIntegration.Id, retiredIntegration.ModuleId, connection.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeFalse("a Retired Integration's binding still counts toward blast radius (§8.5)");
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingIntegrationUnreachable);
    }

    [Fact]
    public async Task Global_unowned_binding_requires_shared_target()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(ResourceContextIds.Shared);
        await h.AddDirectBindingAsync(null, "Sample.Mod", connection.Id); // legacy global binding has no IntegrationId

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.UnownedBindingRequiresShared);
    }

    [Fact]
    public async Task Moving_to_shared_is_never_blocked_by_bindings()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var elsewhere = await h.CreateApplicationAsync("Elsewhere");
        var connection = await h.CreateConnectionAsync(source.Id);
        var boundIntegration = await h.CreateIntegrationAsync(elsewhere.Id); // would normally block a move to `elsewhere`
        await h.AddDirectBindingAsync(boundIntegration.Id, boundIntegration.ModuleId, connection.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, ResourceContextIds.Shared);

        preview.CanMove.Should().BeTrue("every context can reach Shared, so moving to Shared is never blocked by bindings");
    }

    [Fact]
    public async Task Binding_with_missing_integration_is_advisory_not_a_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);
        await h.AddDirectBindingAsync(new IntegrationId(Guid.NewGuid().ToString("N")), "Sample.Mod", connection.Id); // no such Integration row

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Effects.Should().ContainSingle(e => e.Code == ResourceMoveEffectCodes.BindingReferencesMissingIntegration,
            "symmetric with the Integration validator's missing-connection advisory -- a dangling edge is surfaced, never silently skipped");
    }

    [Fact]
    public async Task Multiple_incompatible_integrations_each_produce_their_own_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var connection = await h.CreateConnectionAsync(source.Id);
        var first = await h.CreateIntegrationAsync(thirdParty.Id);
        var second = await h.CreateIntegrationAsync(source.Id);
        await h.AddDirectBindingAsync(first.Id, first.ModuleId, connection.Id);
        await h.AddDirectBindingAsync(second.Id, second.ModuleId, connection.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        var unreachable = preview.Blockers
            .Where(b => b.Code == ResourceMoveBlockerCodes.BindingIntegrationUnreachable)
            .ToList();
        unreachable.Should().HaveCount(2, "every affected Integration is listed -- this is the blast radius the operator must see");
        unreachable.Select(b => b.RelatedResourceId).Should().BeEquivalentTo([first.Id.Value, second.Id.Value]);
    }

    [Fact]
    public async Task Not_authorized_throws()
    {
        await using var h = new ResourceOwnershipMoveTestHarness(grantedAction: null);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        var act = async () => await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Connection_update_permission_is_required_not_resource_context_manage()
    {
        await using var h = new ResourceOwnershipMoveTestHarness(
            grantedAction: Apperture.Core.Domain.Governance.ActionConstants.ResourceContextManage,
            grantedResourceType: Apperture.Core.Domain.Governance.ResourceTypeConstants.ResourceContext);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        var act = async () => await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "holding resource_context.manage must not be enough -- connection.update is the required permission (§9.3)");
    }
}
