using Apperture.Core.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// Operator-facing Retire path: before this, <see cref="IntegrationState.Retired"/> was reachable only
/// through the generic PATCH /integrations/{id} loophole, with no dedicated service method, API route,
/// or UI affordance. Covers the new <c>IIntegrationManagementService.RetireIntegrationAsync</c>.
/// </summary>
public sealed class IntegrationRetireTests
{
    [Fact]
    public async Task RetireIntegrationAsync_FromActive_SetsStateToRetired()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var integration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Active);

        var retired = await harness.Integrations.RetireIntegrationAsync(integration.Id);

        retired.State.Should().Be(IntegrationState.Retired);
    }

    [Fact]
    public async Task RetireIntegrationAsync_FromInactive_SetsStateToRetired()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var integration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Inactive);

        var retired = await harness.Integrations.RetireIntegrationAsync(integration.Id);

        retired.State.Should().Be(IntegrationState.Retired);
    }

    /// <summary>
    /// Closes the loop the CTX-3A archive rule left open: retiring an Integration through the real
    /// operator-facing path (not just constructing it pre-retired in a test) now makes its owning
    /// Application archivable.
    /// </summary>
    [Fact]
    public async Task RetireIntegrationAsync_ThenArchivingItsOwningContext_Succeeds()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var integration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Active);

        await harness.Integrations.RetireIntegrationAsync(integration.Id);
        var archived = await harness.ResourceContexts.ArchiveAsync(context.Id);

        archived.IsActive.Should().BeFalse();
    }
}
