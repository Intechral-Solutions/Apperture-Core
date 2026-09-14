using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// While an Application ResourceContext is archived, nothing it owns may become live again
/// (§6.5, D2). This suite covers the Integration-activation half of that guard.
/// </summary>
public sealed class ResourceContextReactivationGuardTests
{
    [Fact]
    public async Task ActivateIntegration_UnderAnArchivedContext_IsRejected()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var integration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);
        await harness.ResourceContexts.ArchiveAsync(context.Id);

        var act = async () => await harness.Integrations.ActivateIntegrationAsync(integration.Id);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Canvas*archived*");
    }

    [Fact]
    public async Task ActivateIntegration_UnderAnActiveContext_Succeeds()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var integration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);

        var activated = await harness.Integrations.ActivateIntegrationAsync(integration.Id);

        activated.State.Should().Be(IntegrationState.Active);
    }

    [Fact]
    public async Task UpdateIntegration_SettingOtherFieldsUnderAnArchivedContext_IsStillAllowed()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var integration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);
        await harness.ResourceContexts.ArchiveAsync(context.Id);

        var updated = await harness.Integrations.UpdateIntegrationAsync(
            integration.Id, new UpdateIntegrationRequest(DisplayName: "Renamed"));

        updated.DisplayName.Should().Be("Renamed");
    }
}
