using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

public sealed class IntegrationMovePreviewTests
{
    [Fact]
    public async Task Target_not_found_is_a_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var integration = await h.CreateIntegrationAsync(source.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, new(Guid.NewGuid().ToString("N")));

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.TargetNotFound);
    }

    [Fact]
    public async Task Target_archived_is_a_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.ArchiveAsync(target.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.TargetArchived);
    }

    [Fact]
    public async Task Same_target_is_a_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var integration = await h.CreateIntegrationAsync(source.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, source.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.AlreadyInTarget);
    }

    [Fact]
    public async Task Archived_source_is_allowed_cleanup_not_a_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id, Apperture.Core.Domain.Integration.IntegrationState.Retired);
        await h.ArchiveAsync(source.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Blockers.Should().BeEmpty("moving out of an archived Application is legitimate cleanup, not a blocker");
    }

    [Fact]
    public async Task No_bindings_no_schedules_moves_cleanly()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        // Fix round 1, Minor 2: pins that BuildPreview's now-parameterized resourceType argument was
        // wired correctly -- the refactor made a family/resourceType transposition compile where it used
        // to be impossible (each family previously had its own hard-coded Build*Preview helper).
        preview.ResourceType.Should().Be(ResourceMoveResourceTypes.Integration);
        preview.Source.Id.Should().Be(source.Id.Value);
        preview.Target.Id.Should().Be(target.Id.Value);
    }

    [Fact]
    public async Task Connection_already_in_the_target_context_does_not_block()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var connection = await h.CreateConnectionAsync(target.Id); // already where the Integration is going
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Blockers.Should().BeEmpty(
            "the rule is evaluated against the TARGET context, not the source -- a connection already in the target is reachable after the move");
    }

    [Fact]
    public async Task Connection_left_behind_in_the_source_context_blocks()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var connection = await h.CreateConnectionAsync(source.Id); // stays behind in source
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse(
            "CanReference(target, source) is false -- the connection must be moved or unbound first");
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable);
    }

    [Fact]
    public async Task Shared_connection_binding_never_blocks()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var sharedConnection = await h.CreateConnectionAsync(Apperture.Core.Domain.ResourceContexts.ResourceContextIds.Shared);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, sharedConnection.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Blockers.Should().BeEmpty();
    }

    [Fact]
    public async Task Unreachable_target_binding_blocks()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var connection = await h.CreateConnectionAsync(thirdParty.Id); // neither source nor target
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        var blocker = preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable).Subject;
        blocker.RelatedResourceId.Should().Be(connection.Id.Value);
    }

    [Fact]
    public async Task Moving_to_shared_requires_every_bound_connection_to_be_shared()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var sameContextConnection = await h.CreateConnectionAsync(source.Id); // not Shared
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, sameContextConnection.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, Apperture.Core.Domain.ResourceContexts.ResourceContextIds.Shared);

        preview.CanMove.Should().BeFalse("Shared cannot reach an Application-owned connection");
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable);
    }

    [Fact]
    public async Task Retired_connection_binding_evaluated_the_same_way()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var retiredConnection = await h.CreateConnectionAsync(source.Id, Apperture.Core.Domain.Connector.ConnectionState.Retired);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, retiredConnection.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse("a Retired connection is still evaluated by the reference rule, not skipped");
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable);
    }

    [Fact]
    public async Task Binding_with_missing_connection_is_advisory_not_a_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, new(Guid.NewGuid().ToString("N"))); // no such Connection row

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Effects.Should().ContainSingle(e => e.Code == ResourceMoveEffectCodes.BindingReferencesMissingConnection);
    }

    [Fact]
    public async Task Capability_binding_with_null_connection_id_previews_cleanly()
    {
        // Part E1: the validator's `if (binding.ConnectionId is not { } connectionId) continue;` branch
        // (a capability binding with no Connection to check at all) had no test reaching it. This is
        // distinct from Binding_with_missing_connection_is_advisory_not_a_blocker, where ConnectionId is
        // set but the referenced Connection row does not exist -- conflating "no Connection" with "missing
        // Connection" would be a real defect (a spurious advisory effect for a binding that was never
        // supposed to have a Connection in the first place).
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connectionId: null);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        preview.Blockers.Should().BeEmpty();
        preview.Effects.Should().NotContain(e => e.Code == ResourceMoveEffectCodes.BindingReferencesMissingConnection,
            "a capability binding with no ConnectionId at all is a different case from one whose Connection row is missing -- the former is not even advisory-worthy");
    }

    [Fact]
    public async Task Attached_schedules_move_effect_lists_schedule_count()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.CreateAttachedScheduleAsync(integration.Id);
        await h.CreateAttachedScheduleAsync(integration.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
        var effect = preview.Effects.Should().ContainSingle(e => e.Code == ResourceMoveEffectCodes.SchedulesMoveWithIntegration).Subject;
        effect.RelatedResourceIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task Inconsistent_schedule_context_blocks()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var drifted = await h.CreateApplicationAsync("Drifted");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var schedule = await h.CreateAttachedScheduleAsync(integration.Id);
        await h.SetScheduleResourceContextDirectlyAsync(schedule.Id, drifted.Id); // simulate pre-existing corruption

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.ScheduleContextInconsistent);
    }

    [Fact]
    public async Task Retired_and_inactive_integrations_remain_movable()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var retired = await h.CreateIntegrationAsync(source.Id, IntegrationState.Retired);
        var inactive = await h.CreateIntegrationAsync(source.Id, IntegrationState.Inactive);

        (await h.Service.PreviewIntegrationMoveAsync(retired.Id, target.Id)).CanMove.Should().BeTrue();
        (await h.Service.PreviewIntegrationMoveAsync(inactive.Id, target.Id)).CanMove.Should().BeTrue();
    }

    [Fact]
    public async Task Not_authorized_throws()
    {
        await using var h = new ResourceOwnershipMoveTestHarness(grantedAction: null);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        var act = async () => await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Not_authorized_takes_priority_over_unknown_integration_id()
    {
        // Regression coverage for the actor-before-lookup ordering fix. ResourceOwnershipMoveTestHarness's
        // own FixedPrincipalContext always returns a non-null actor (its grantedAction: null configures
        // permission DENIAL, not the absence of an actor), so it cannot exercise the true "no authenticated
        // actor" path that occurs in production before authentication middleware attaches one. This test
        // therefore builds a second service instance directly over the harness's real repositories with a
        // principal context that returns no actor at all, to prove the actor check now happens strictly
        // before the integration lookup: an unauthenticated caller must not be able to distinguish a
        // missing integration from an unauthorized one via exception type.
        await using var h = new ResourceOwnershipMoveTestHarness();
        var target = await h.CreateApplicationAsync("Canvas");
        var service = new ResourceOwnershipMoveService(
            new NoActorPrincipalContext(),
            new DenyAllAuthorizationEvaluationService(),
            h.IntegrationRepository,
            h.ConnectionRepository,
            h.ScheduleRepository,
            h.BindingRepository,
            h.ResourceContexts);

        var act = async () => await service.PreviewIntegrationMoveAsync(new(Guid.NewGuid().ToString("N")), target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "an unauthenticated caller must not be able to distinguish a missing integration from an unauthorized one");
    }

    [Fact]
    public async Task Integration_update_permission_is_required_not_resource_context_manage()
    {
        await using var h = new ResourceOwnershipMoveTestHarness(
            grantedAction: Apperture.Core.Domain.Governance.ActionConstants.ResourceContextManage,
            grantedResourceType: Apperture.Core.Domain.Governance.ResourceTypeConstants.ResourceContext);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        var act = async () => await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "holding resource_context.manage must not be enough -- integration.update is the required permission (§9.3)");
    }

    [Fact]
    public async Task Integration_update_permission_is_sufficient()
    {
        // Positive counterpart to Integration_update_permission_is_required_not_resource_context_manage:
        // every other test in this file builds the harness with the default grantedAction: "any", which
        // short-circuits permission matching entirely. This pins the ACTUAL action/resource-type the
        // service requests, not merely that some unrelated permission is insufficient.
        await using var h = new ResourceOwnershipMoveTestHarness(
            grantedAction: Apperture.Core.Domain.Governance.ActionConstants.IntegrationUpdate,
            grantedResourceType: Apperture.Core.Domain.Governance.ResourceTypeConstants.Integration);
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeTrue();
    }

    [Fact]
    public async Task Multiple_unreachable_connections_each_produce_their_own_blocker()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var first = await h.CreateConnectionAsync(thirdParty.Id);
        var second = await h.CreateConnectionAsync(source.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, first.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, second.Id);

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        var unreachable = preview.Blockers
            .Where(b => b.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable)
            .ToList();
        unreachable.Should().HaveCount(2,
            "every offending connection must be reported -- collapsing to the first one hides half the work the operator has to do");
        unreachable.Select(b => b.RelatedResourceId)
            .Should().BeEquivalentTo([first.Id.Value, second.Id.Value]);
        unreachable.Should().OnlyContain(b => b.RelatedDisplayName != null && b.RelatedResourceContextId != null,
            "each blocker carries its own related resource, not a shared summary");
    }

    [Fact]
    public async Task Same_connection_bound_twice_produces_one_blocker()
    {
        // Review fix round 1, Important 2 (mirror of the Connection validator's fix): an Integration bound
        // to the SAME Connection across two slots is an ordinary configuration, not two distinct
        // blast-radius entries. Before the fix, this fixture produced two byte-identical
        // binding_connection_unreachable blockers for one Connection.
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var connection = await h.CreateConnectionAsync(thirdParty.Id); // neither source nor target
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id); // second slot, same Connection

        var preview = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable,
            "one distinct Connection must produce exactly one blocker, no matter how many bindings reference it");
    }

    [Fact]
    public async Task Blocker_order_is_deterministic_across_repeated_previews()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var integration = await h.CreateIntegrationAsync(source.Id);

        // Capture each binding's own id alongside its connection's id, so the expected order below is
        // derived from the real (ordinal, not insertion) binding-id sort rather than merely asserting the
        // two preview calls agree with EACH OTHER -- EF InMemory returns rows in stable insertion order on
        // every call, so a self-consistency-only assertion would stay green even if the service's OrderBy
        // were deleted entirely.
        var bindingToConnection = new List<(string BindingId, string ConnectionId)>();
        for (var i = 0; i < 5; i++)
        {
            var connection = await h.CreateConnectionAsync(thirdParty.Id);
            var binding = await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);
            bindingToConnection.Add((binding.Id.Value, connection.Id.Value));
        }

        var expectedOrder = bindingToConnection
            .OrderBy(p => p.BindingId, StringComparer.Ordinal)
            .Select(p => p.ConnectionId)
            .ToList();

        var first = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);
        var second = await h.Service.PreviewIntegrationMoveAsync(integration.Id, target.Id);

        first.Blockers.Should().HaveCount(5);
        first.Blockers.Select(b => b.RelatedResourceId).Should().Equal(expectedOrder,
            "blockers must be reported in ordinal binding-id order, not database insertion order");
        first.Blockers.Select(b => b.RelatedResourceId)
            .Should().Equal(second.Blockers.Select(b => b.RelatedResourceId),
                "blocker order must be stable so the API response and the UI dialog do not reshuffle between calls");
    }

    /// <summary>Simulates the pre-authentication state (no actor resolved yet), which
    /// <see cref="ResourceOwnershipMoveTestHarness"/>'s own <c>FixedPrincipalContext</c> cannot produce.</summary>
    private sealed class NoActorPrincipalContext : IPrincipalContext
    {
        public ValueTask<Apperture.Shared.Kernel.IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Apperture.Shared.Kernel.IActor?>(null);
    }

    private sealed class DenyAllAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(
            Apperture.Core.Domain.Governance.AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }
}
