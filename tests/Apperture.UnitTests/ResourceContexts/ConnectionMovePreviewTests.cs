using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

public sealed class ConnectionMovePreviewTests
{
    // --- Part A1: common blockers, own copies for the Connection family ---
    // ValidateConnectionMoveAsync is a separate code path from ValidateIntegrationMoveAsync with its own
    // copies of the three common branches (target_not_found, target_archived, already_in_target). Each
    // needs its own coverage; the Integration family's coverage does not exercise this code path at all.

    [Fact]
    public async Task Target_not_found_is_a_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var connection = await h.CreateConnectionAsync(source.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, new(Guid.NewGuid().ToString("N")));

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.TargetNotFound);
    }

    [Fact]
    public async Task Target_archived_is_a_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);
        await h.ArchiveAsync(target.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.TargetArchived);
    }

    [Fact]
    public async Task Same_target_is_a_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var connection = await h.CreateConnectionAsync(source.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, source.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.AlreadyInTarget);
    }

    // --- Part A2: Inactive bound Integration also blocks (design gate §8.5 step 2: "including those of
    // Retired and Inactive Integrations"). Sibling to Retired_integration_bindings_still_count, which
    // already correctly names only the Retired case it covers. ---

    [Fact]
    public async Task Inactive_integration_bindings_still_count()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var connection = await h.CreateConnectionAsync(source.Id);
        var inactiveIntegration = await h.CreateIntegrationAsync(thirdParty.Id, IntegrationState.Inactive);
        await h.AddDirectBindingAsync(inactiveIntegration.Id, inactiveIntegration.ModuleId, connection.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeFalse("an Inactive Integration's binding still counts toward blast radius (design gate §8.5 step 2: all bindings count, including those of Retired and Inactive Integrations)");
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingIntegrationUnreachable);
    }

    // --- Part A3: deterministic "Used by" ordering, mirror of the Integration family's
    // Blocker_order_is_deterministic_across_repeated_previews. ---

    [Fact]
    public async Task Used_by_effect_order_is_deterministic_across_repeated_previews()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        // Bind five distinct Integrations, all already in the target context, so the preview succeeds and
        // the "used by" effect (not a blocker) is the interesting output. Capture each binding's own id
        // alongside its Integration's id so the expected order is derived from the real (ordinal, not
        // insertion) binding-id sort, not merely self-consistency between the two preview calls -- EF
        // InMemory returns rows in stable insertion order on every call, so a self-consistency-only
        // assertion would stay green even if the service's OrderBy were deleted entirely.
        var bindingToIntegration = new List<(string BindingId, string IntegrationId)>();
        for (var i = 0; i < 5; i++)
        {
            var integration = await h.CreateIntegrationAsync(target.Id);
            var binding = await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);
            bindingToIntegration.Add((binding.Id.Value, integration.Id.Value));
        }

        var expectedOrder = bindingToIntegration
            .OrderBy(p => p.BindingId, StringComparer.Ordinal)
            .Select(p => p.IntegrationId)
            .ToList();

        var first = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);
        var second = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        first.CanMove.Should().BeTrue();
        var usedByFirst = first.Effects.Should().ContainSingle(e => e.Code == ResourceMoveEffectCodes.UsedByIntegration).Subject;
        usedByFirst.RelatedResourceIds.Should().HaveCount(5);
        usedByFirst.RelatedResourceIds.Should().Equal(expectedOrder,
            "used-by order must follow ordinal binding-id order, not database insertion order");

        var usedBySecond = second.Effects.Should().ContainSingle(e => e.Code == ResourceMoveEffectCodes.UsedByIntegration).Subject;
        usedByFirst.RelatedResourceIds.Should().Equal(usedBySecond.RelatedResourceIds,
            "used-by order must be stable so the API response and the UI dialog do not reshuffle between calls");
    }

    [Fact]
    public async Task No_bindings_moves_cleanly()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        // Fix round 1, Minor 2: pins that BuildPreview's now-parameterized resourceType argument was
        // wired correctly -- the refactor made a family/resourceType transposition compile where it used
        // to be impossible (each family previously had its own hard-coded Build*Preview helper).
        preview.ResourceType.Should().Be(ResourceMoveResourceTypes.Connection);
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
    public async Task Retired_integration_bindings_still_count()
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
    public async Task Unowned_binding_moving_to_shared_is_not_blocked()
    {
        // Only the true arm of `!ResourceContextIds.IsShared(targetId)` was covered before this fix
        // (review fix round 1, Minor 4) -- inverting that condition would have left every other test green.
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var connection = await h.CreateConnectionAsync(source.Id);
        await h.AddDirectBindingAsync(null, "Sample.Mod", connection.Id); // legacy global binding has no IntegrationId

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, ResourceContextIds.Shared);

        preview.CanMove.Should().BeTrue("a legacy global binding is compatible with Shared, which is exactly where it must end up");
        preview.Blockers.Should().NotContain(b => b.Code == ResourceMoveBlockerCodes.UnownedBindingRequiresShared);
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
        preview.Blockers.Should().BeEmpty();
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
        preview.Effects.Should().NotContain(e => e.Code == ResourceMoveEffectCodes.UsedByIntegration,
            "a missing Integration is never counted in the blast radius -- naming a phantom in \"Used by\" would mislead the operator (review fix round 1, Minor 3)");
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
    public async Task Same_integration_bound_twice_produces_one_blocker_and_one_used_by_entry()
    {
        // Review fix round 1, Important 2: a Connection bound into two slots of the SAME Integration (an
        // ordinary configuration -- e.g. a source slot and a target slot sharing a connection) must be
        // reported as ONE affected Integration, not two. Before the fix, this fixture produced
        // RelatedResourceIds = [X, X] and "Used by 2 integration(s)" for a single Integration.
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var connection = await h.CreateConnectionAsync(source.Id);
        var integration = await h.CreateIntegrationAsync(thirdParty.Id); // not source, not target
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id); // second slot, same Integration

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingIntegrationUnreachable,
            "one distinct Integration must produce exactly one blocker, no matter how many bindings reference it");
    }

    [Fact]
    public async Task Repeated_integration_binding_still_reports_used_by_once()
    {
        // Companion to Same_integration_bound_twice_produces_one_blocker_and_one_used_by_entry, pinning the
        // "Used by" payload specifically (a compatible Integration bound twice must not double-count).
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id); // second slot, same Integration
        await h.MoveIntegrationDirectlyForFixtureAsync(integration.Id, target.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        var usedBy = preview.Effects.Should().ContainSingle(e => e.Code == ResourceMoveEffectCodes.UsedByIntegration).Subject;
        usedBy.RelatedResourceIds.Should().ContainSingle().Which.Should().Be(integration.Id.Value,
            "two bindings into the same Integration is one Integration in the blast radius, not two");
        usedBy.Message.Should().Contain("Used by 1 integration");
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

    [Fact]
    public async Task Connection_update_permission_is_sufficient()
    {
        // Positive counterpart to Connection_update_permission_is_required_not_resource_context_manage
        // (review fix round 1, Important 1; mirrors the Integration family's
        // Integration_update_permission_is_sufficient from fix commit f7bc887): every other test in this
        // file builds the harness with the default grantedAction: "any", which short-circuits permission
        // matching entirely. This pins the ACTUAL action/resource-type the service requests -- if the
        // service asked for the wrong action, or Connection.GetIdentity() returned the wrong resource
        // type, this test (and only this test) would fail.
        await using var h = new ResourceOwnershipMoveTestHarness(
            grantedAction: Apperture.Core.Domain.Governance.ActionConstants.ConnectionUpdate,
            grantedResourceType: Apperture.Core.Domain.Governance.ResourceTypeConstants.Connection);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        var preview = await h.Service.PreviewConnectionMoveAsync(connection.Id, target.Id);

        preview.CanMove.Should().BeTrue();
    }
}
