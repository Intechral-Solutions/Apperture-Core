using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Per-family live/retired-or-disabled ownership summaries (CTX-3A §6.5, decision D2), computed via the
/// real SQL repository over EF Core InMemory rather than mocked.
/// </summary>
public sealed class ResourceContextOwnershipSummaryTests
{
    [Fact]
    public async Task GetOwnershipSummaryAsync_CountsLiveAndRetiredPerFamilySeparately()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");

        await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Active);
        await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);
        await harness.CreateConnectionAsync(context.Id, state: ConnectionState.Active);
        await harness.CreateConnectionAsync(context.Id, state: ConnectionState.Retired);
        await harness.CreateStandaloneScheduleAsync(context.Id, enabled: true);
        await harness.CreateStandaloneScheduleAsync(context.Id, enabled: false);

        var summary = await harness.ResourceContextRepository.GetOwnershipSummaryAsync(context.Id, CancellationToken.None);

        summary.LiveIntegrations.Should().Be(1);
        summary.RetiredIntegrations.Should().Be(1);
        summary.ActiveConnections.Should().Be(1);
        summary.RetiredConnections.Should().Be(1);
        summary.EnabledSchedules.Should().Be(1);
        summary.DisabledSchedules.Should().Be(1);
        summary.BlocksArchive.Should().BeTrue();
    }

    /// <summary>
    /// C1 regression: <see cref="IntegrationState.Inactive"/> is neither Active nor Retired.
    /// §6.5's archive rule is "no Integration whose state is not Retired" -- Inactive integrations
    /// must therefore count as live (they block archive), not fall through both buckets uncounted.
    /// </summary>
    [Fact]
    public async Task GetOwnershipSummaryAsync_WithAnInactiveIntegration_CountsAsLiveAndBlocksArchive()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Inactive);

        var summary = await harness.ResourceContextRepository.GetOwnershipSummaryAsync(context.Id, CancellationToken.None);

        summary.LiveIntegrations.Should().Be(1);
        summary.RetiredIntegrations.Should().Be(0);
        summary.BlocksArchive.Should().BeTrue();
    }

    /// <summary>
    /// Guards the aggregation itself: Active and Inactive integrations are two distinct
    /// (ResourceContextId, State) groups from the underlying GROUP BY, and both must be summed into
    /// one LiveIntegrations count for the context rather than colliding on a dictionary key.
    /// </summary>
    [Fact]
    public async Task GetOwnershipSummaryAsync_WithActiveAndInactiveIntegrations_SumsBothAsLive()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Active);
        await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Inactive);
        await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);

        var summary = await harness.ResourceContextRepository.GetOwnershipSummaryAsync(context.Id, CancellationToken.None);

        summary.LiveIntegrations.Should().Be(2);
        summary.RetiredIntegrations.Should().Be(1);
        summary.BlocksArchive.Should().BeTrue();
    }

    /// <summary>
    /// The archive refusal must describe an Inactive Integration as "not retired," never as "active" --
    /// Inactive blocks archive for the same reason Active does (neither is Retired), and calling it
    /// "active" would mislead an operator into thinking Deactivate alone clears the blocker.
    /// </summary>
    [Fact]
    public async Task ArchiveAsync_WithAnInactiveIntegration_RefusalDoesNotCallItActive()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Inactive);

        var act = async () => await harness.ResourceContexts.ArchiveAsync(context.Id);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("1 integration not retired");
        ex.Which.Message.Should().NotContain("active integration");
    }

    [Fact]
    public async Task GetOwnershipSummaryAsync_WithOnlyRetiredAndDisabled_DoesNotBlockArchive()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);
        await harness.CreateConnectionAsync(context.Id, state: ConnectionState.Retired);
        await harness.CreateStandaloneScheduleAsync(context.Id, enabled: false);

        var summary = await harness.ResourceContextRepository.GetOwnershipSummaryAsync(context.Id, CancellationToken.None);

        summary.BlocksArchive.Should().BeFalse();
    }

    [Fact]
    public async Task GetOwnershipSummaryAsync_ForAContextThatOwnsNothing_ReturnsAllZeros()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");

        var summary = await harness.ResourceContextRepository.GetOwnershipSummaryAsync(context.Id, CancellationToken.None);

        summary.Should().Be(new ResourceContextOwnershipSummary(0, 0, 0, 0, 0, 0));
        summary.BlocksArchive.Should().BeFalse();
    }

    [Fact]
    public async Task GetOwnershipSummariesAsync_RunsAConstantNumberOfQueriesRegardlessOfContextCount()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var contextA = await harness.ResourceContexts.CreateApplicationContextAsync("A");
        var contextB = await harness.ResourceContexts.CreateApplicationContextAsync("B");
        await harness.CreateIntegrationAsync(contextA.Id, state: IntegrationState.Active);
        await harness.CreateIntegrationAsync(contextB.Id, state: IntegrationState.Active);

        var (countingRepository, queryCount) = harness.CreateQueryCountingRepository();
        var queryCountBefore = queryCount();
        var summaries = await countingRepository.GetOwnershipSummariesAsync(CancellationToken.None);
        var queriesIssued = queryCount() - queryCountBefore;

        summaries[contextA.Id.Value].LiveIntegrations.Should().Be(1);
        summaries[contextB.Id.Value].LiveIntegrations.Should().Be(1);
        queriesIssued.Should().BeLessThanOrEqualTo(3,
            "GetOwnershipSummariesAsync should issue one GROUP BY per family (Integrations, Connections, " +
            "ScheduleDefinitions), independent of context count -- not an N+1 pattern");
    }
}
