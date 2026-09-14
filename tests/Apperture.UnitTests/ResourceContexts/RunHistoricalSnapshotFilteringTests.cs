using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3A Plan 4 Task 5 (§5.3, §14): the design gate's named regression test. Proves that
/// <see cref="RunQueryService.ListRunsAsync"/> filters on each Run's own immutable
/// <c>ResourceContextId</c> snapshot (CTX-2 semantics), never on the owning Integration's current
/// ResourceContext -- even after that ownership changes underneath an already-dispatched Run.
/// </summary>
public sealed class RunHistoricalSnapshotFilteringTests
{
    [Fact]
    public async Task RunSnapshottedUnderA_StillListsUnderA_AfterIntegrationsCurrentOwnershipBecomesB()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var contextA = await harness.ResourceContexts.CreateApplicationContextAsync("A");
        var contextB = await harness.ResourceContexts.CreateApplicationContextAsync("B");
        var integration = await harness.CreateIntegrationAsync(contextA.Id, state: IntegrationState.Active);

        // Simulate a run dispatched while the integration was owned by A (CTX-2 snapshot semantics).
        await harness.SeedOperationRunAsync(integrationId: integration.Id, resourceContextId: contextA.Id);

        // Controlled test setup: directly rewrite the Integration's current ownership to B,
        // bypassing any CTX-3B movement machinery (which does not exist yet) -- this is purely
        // to prove the Run's own snapshot column, not the Integration's current column, drives the filter.
        await harness.SetIntegrationResourceContextDirectlyAsync(integration.Id, contextB.Id);

        var runsUnderA = await harness.RunQueryService.ListRunsAsync(new RunQueryFilter(ResourceContextId: contextA.Id.Value));
        var runsUnderB = await harness.RunQueryService.ListRunsAsync(new RunQueryFilter(ResourceContextId: contextB.Id.Value));

        runsUnderA.Should().ContainSingle();
        runsUnderB.Should().BeEmpty();
    }

    [Fact]
    public async Task NullSnapshotRuns_AppearOnlyUnderAll()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        await harness.SeedOperationRunAsync(integrationId: null, resourceContextId: null);

        var underAll = await harness.RunQueryService.ListRunsAsync(new RunQueryFilter());
        var underShared = await harness.RunQueryService.ListRunsAsync(new RunQueryFilter(ResourceContextId: "shared"));

        underAll.Should().ContainSingle();
        underShared.Should().BeEmpty();
    }
}
