using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Design gate §14: no move may ever rewrite a CTX-2 historical snapshot. CTX-2 records (OperationRuns,
/// WorkflowInstances, audit logs, etc.) capture which ResourceContext/Integration owned a resource AT THE
/// TIME something happened; a move changes CURRENT ownership only, and must never be silently "followed"
/// by rewriting history to match. These tests run the production <c>MoveIntegrationAsync</c>/
/// <c>MoveConnectionAsync</c>/<c>MoveScheduleAsync</c> (Task 6), never a reimplementation, and assert every
/// historical family from <see cref="ResourceOwnershipMoveTestHarness.CaptureHistoricalProvenanceAsync"/>
/// is byte-identical before and after -- for both the successful and the blocked outcome.
/// </summary>
public sealed class ResourceMoveHistoricalImmutabilityTests
{
    [Fact]
    public async Task Integration_move_never_rewrites_any_CTX2_historical_snapshot()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.CreateAttachedScheduleAsync(integration.Id);

        // Seed one row per historical family, every one snapshotted to the SOURCE context, so a move that
        // wrongly "followed" current ownership would visibly rewrite them to the target.
        await h.SeedHistoricalProvenanceAsync(integration.Id, source.Id);
        var before = await h.CaptureHistoricalProvenanceAsync();

        var result = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);
        result.CanMove.Should().BeTrue();

        var after = await h.CaptureHistoricalProvenanceAsync();
        after.Should().BeEquivalentTo(before,
            "CTX-2 provenance records what happened, not what is currently owned -- a move must never rewrite it");
    }

    [Fact]
    public async Task Blocked_integration_move_never_rewrites_any_CTX2_historical_snapshot()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var thirdParty = await h.CreateApplicationAsync("Workday");
        var integration = await h.CreateIntegrationAsync(source.Id);
        await h.CreateAttachedScheduleAsync(integration.Id);
        var connection = await h.CreateConnectionAsync(thirdParty.Id);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        await h.SeedHistoricalProvenanceAsync(integration.Id, source.Id);
        var before = await h.CaptureHistoricalProvenanceAsync();

        var result = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);
        result.CanMove.Should().BeFalse("the bound Connection in a third context makes the target unreachable");

        var after = await h.CaptureHistoricalProvenanceAsync();
        after.Should().BeEquivalentTo(before,
            "a blocked move must not rewrite CTX-2 historical provenance either -- atomicity cuts both ways");
    }

    [Fact]
    public async Task Connection_move_never_rewrites_any_CTX2_historical_snapshot()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var connection = await h.CreateConnectionAsync(source.Id);

        // No historical family carries a ConnectionId, so this seeds provenance for an integration that is
        // merely CO-LOCATED in the same source context -- catching a naive "rewrite every row whose
        // ResourceContextId equals source" bug that a per-entity-type move implementation should never do.
        var coLocatedIntegration = await h.CreateIntegrationAsync(source.Id);
        await h.SeedHistoricalProvenanceAsync(coLocatedIntegration.Id, source.Id);
        var before = await h.CaptureHistoricalProvenanceAsync();

        var result = await h.Service.MoveConnectionAsync(connection.Id, target.Id);
        result.CanMove.Should().BeTrue();

        var after = await h.CaptureHistoricalProvenanceAsync();
        after.Should().BeEquivalentTo(before,
            "a Connection move must never rewrite CTX-2 historical provenance, including rows merely co-located in the source context");
    }

    [Fact]
    public async Task Standalone_schedule_move_never_rewrites_any_CTX2_historical_snapshot()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(source.Id);

        var coLocatedIntegration = await h.CreateIntegrationAsync(source.Id);
        await h.SeedHistoricalProvenanceAsync(coLocatedIntegration.Id, source.Id);
        var before = await h.CaptureHistoricalProvenanceAsync();

        var result = await h.Service.MoveScheduleAsync(schedule.Id, target.Id);
        result.CanMove.Should().BeTrue();

        var after = await h.CaptureHistoricalProvenanceAsync();
        after.Should().BeEquivalentTo(before,
            "a standalone Schedule move must never rewrite CTX-2 historical provenance");
    }

    [Fact]
    public async Task Move_appends_to_audit_without_rewriting_any_pre_existing_audit_row()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);

        h.Db.AuditLogs.Add(new AuditLogEntity
        {
            At = DateTimeOffset.UtcNow,
            Actor = "pre-existing-actor",
            Action = "pre.existing.action",
            ResourceContextId = source.Id.Value
        });
        await h.Db.SaveChangesAsync();
        var before = await h.Db.AuditLogs.AsNoTracking().OrderBy(a => a.Id).ToListAsync();

        var result = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);
        result.CanMove.Should().BeTrue();

        var after = await h.Db.AuditLogs.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        var beforeIds = before.Select(r => r.Id).ToHashSet();

        after.Where(a => beforeIds.Contains(a.Id)).Should().BeEquivalentTo(before,
            "pre-existing audit rows are historical fact and must never be rewritten by a later move");
        after.Count.Should().BeGreaterThanOrEqualTo(before.Count,
            "Plan 2 writes no audit rows today (Plan 4 will), so a move may only ever append rows, never remove or alter them");
    }

    [Fact]
    public async Task Integration_move_does_not_change_the_bindings_it_evaluated()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        // Shared, so the bound Connection stays reachable from the target and the move succeeds --
        // isolating whether a successful move mutates the binding edge itself.
        var connection = await h.CreateConnectionAsync(ResourceContextIds.Shared);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, connection.Id);

        var before = await h.Db.Bindings.AsNoTracking().OrderBy(b => b.Id).ToListAsync();

        var result = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);
        result.CanMove.Should().BeTrue();

        var after = await h.Db.Bindings.AsNoTracking().OrderBy(b => b.Id).ToListAsync();
        after.Should().BeEquivalentTo(before,
            "bindings are edges the move evaluates, not ownership snapshots it mutates -- ConnectionId/IntegrationId must be untouched");
    }
}
