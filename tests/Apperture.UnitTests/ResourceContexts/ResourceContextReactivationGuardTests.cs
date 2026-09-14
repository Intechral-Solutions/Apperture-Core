using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
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

    /// <summary>
    /// Same rule, applied to Schedule enablement: enabling a Schedule owned by an archived
    /// Application ResourceContext would make it live again, which §6.5/D2 forbids.
    /// </summary>
    [Fact]
    public async Task EnableSchedule_UnderAnArchivedContext_IsRejected()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var schedule = await harness.CreateStandaloneScheduleAsync(context.Id, enabled: false);
        await harness.ResourceContexts.ArchiveAsync(context.Id);

        var act = async () => await harness.Schedules.EnableAsync(schedule.Id);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Canvas*archived*");
    }

    /// <summary>
    /// Closes the inherit-on-create gap (§6.5, D2): creating an Integration-attached Schedule with
    /// no explicit ResourceContext inherits the Integration's context without going through
    /// <c>ResolveForNewOwnershipAsync</c>'s Active check, unlike the standalone and
    /// explicit-and-matching branches. An enabled schedule inheriting an archived context is
    /// effectively reactivating it, so it must be rejected the same way.
    /// </summary>
    [Fact]
    public async Task CreateIntegrationAttachedSchedule_InheritingAnArchivedIntegrationContext_IsRejected()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var integration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);
        await harness.ResourceContexts.ArchiveAsync(context.Id);

        var definition = new ScheduleDefinition(
            new ScheduleId("s-inherit-archived"),
            "Nightly",
            "0 0 * * *",
            "UTC",
            Enabled: true,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            ExecutionPolicyRef: null,
            Version: 1,
            IntegrationId: integration.Id);

        var act = async () => await harness.Schedules.CreateScheduleAsync(definition);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Canvas*archived*");
    }

    /// <summary>
    /// Creating a <b>disabled</b> Integration-attached schedule under an archived context's
    /// Integration is not a reactivation — it does not make anything live — so it must remain
    /// allowed, unlike the enabled case above.
    /// </summary>
    [Fact]
    public async Task CreateIntegrationAttachedSchedule_DisabledInheritingAnArchivedIntegrationContext_IsStillAllowed()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var integration = await harness.CreateIntegrationAsync(context.Id, state: IntegrationState.Retired);
        await harness.ResourceContexts.ArchiveAsync(context.Id);

        var definition = new ScheduleDefinition(
            new ScheduleId("s-inherit-archived-disabled"),
            "Nightly",
            "0 0 * * *",
            "UTC",
            Enabled: false,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            ExecutionPolicyRef: null,
            Version: 1,
            IntegrationId: integration.Id);

        var created = await harness.Schedules.CreateScheduleAsync(definition);

        created.ResourceContextId!.Value.Should().Be(context.Id.Value);
    }
}
