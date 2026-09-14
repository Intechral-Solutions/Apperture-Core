using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Connector;
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

        (await act.Should().ThrowAsync<ResourceContextArchivedException>()).WithMessage("*Canvas*archived*");
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

        (await act.Should().ThrowAsync<ResourceContextArchivedException>()).WithMessage("*Canvas*archived*");
    }

    /// <summary>
    /// Positive-path check for the guard above: enabling a Schedule owned by an active (never
    /// archived) ResourceContext must succeed exactly as before the guard was added.
    /// </summary>
    [Fact]
    public async Task EnableSchedule_UnderAnActiveContext_Succeeds()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var schedule = await harness.CreateStandaloneScheduleAsync(context.Id, enabled: false);

        await harness.Schedules.EnableAsync(schedule.Id);

        var updated = await harness.Schedules.GetDefinitionAsync(schedule.Id);
        updated!.Enabled.Should().BeTrue();
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

        (await act.Should().ThrowAsync<ResourceContextArchivedException>()).WithMessage("*Canvas*archived*");
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

    /// <summary>
    /// CTX-3A finding C2: EnableAsync's guard was bypassable via PATCH /schedules/{id} because
    /// UpdateScheduleAsync persisted Enabled verbatim with no context check. Flipping a Schedule's
    /// Enabled from false to true through UpdateScheduleAsync is the exact same reactivation
    /// EnableAsync already guards, so it must be rejected identically.
    /// </summary>
    [Fact]
    public async Task UpdateSchedule_FlippingEnabledToTrueUnderAnArchivedContext_IsRejected()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var schedule = await harness.CreateStandaloneScheduleAsync(context.Id, enabled: false);
        await harness.ResourceContexts.ArchiveAsync(context.Id);

        var act = async () => await harness.Schedules.UpdateScheduleAsync(schedule with { Enabled = true });

        (await act.Should().ThrowAsync<ResourceContextArchivedException>()).WithMessage("*Canvas*archived*");
    }

    /// <summary>
    /// Positive-path check for C2's guard: flipping Enabled false -> true via UpdateScheduleAsync under
    /// an active (never archived) context must succeed exactly as before the guard was added.
    /// </summary>
    [Fact]
    public async Task UpdateSchedule_FlippingEnabledToTrueUnderAnActiveContext_Succeeds()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var schedule = await harness.CreateStandaloneScheduleAsync(context.Id, enabled: false);

        await harness.Schedules.UpdateScheduleAsync(schedule with { Enabled = true });

        var updated = await harness.Schedules.GetDefinitionAsync(schedule.Id);
        updated!.Enabled.Should().BeTrue();
    }

    /// <summary>
    /// Only the false -&gt; true transition is a reactivation. An ordinary field update on an already-
    /// disabled Schedule (leaving Enabled false, i.e. false -&gt; false) makes nothing live and must
    /// remain allowed under an archived context, the same way Integration's other-field update stays
    /// allowed above.
    /// </summary>
    [Fact]
    public async Task UpdateSchedule_SettingOtherFieldsWhileStayingDisabledUnderAnArchivedContext_IsStillAllowed()
    {
        await using var harness = new ResourceContextOwnershipTestHarness();
        var context = await harness.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var schedule = await harness.CreateStandaloneScheduleAsync(context.Id, enabled: false);
        await harness.ResourceContexts.ArchiveAsync(context.Id);

        await harness.Schedules.UpdateScheduleAsync(schedule with { Name = "Renamed" });

        var updated = await harness.Schedules.GetDefinitionAsync(schedule.Id);
        updated!.Name.Should().Be("Renamed");
        updated.Enabled.Should().BeFalse();
    }

    [Fact]
    public void ConnectionManagementService_HasNoReactivationPath_SoNoGuardIsNeededYet()
    {
        // Verified during CTX-3A: ConnectionState is one-way (Active -> Retired only).
        // RetireConnectionAsync only ever sets State = Retired; UpdateConnectionAsync never touches State.
        // There is no un-retire/reactivate method on IConnectionManagementService.
        // If this enum ever gains a reverse transition, a reactivation guard analogous to
        // IntegrationManagementService.UpdateIntegrationAsync's must be added at that time.
        var connectionStateNames = Enum.GetNames<ConnectionState>();

        connectionStateNames.Should().BeEquivalentTo(["Active", "Retired"],
            "this test intentionally breaks if ConnectionState grows a reverse transition, " +
            "as a prompt to add the CTX-3A reactivation guard for Connections");
    }
}
