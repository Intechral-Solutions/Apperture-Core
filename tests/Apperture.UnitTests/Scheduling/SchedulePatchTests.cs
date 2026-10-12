using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using Apperture.UnitTests.ResourceContexts;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Scheduling;

/// <summary>
/// AUD-1.4 slice 4 remediation of review finding M1: <see cref="SqlScheduleManagementService.PatchScheduleAsync"/> applies only
/// the fields a PATCH carries, over the schedule as locked, so a field it leaves out is never restored from an earlier read. The
/// interleavings themselves are proven against SQL Server in <c>SchedulePatchSqlServerTests</c>; these cases pin the semantics.
/// </summary>
public sealed class SchedulePatchTests
{
    [Fact]
    public async Task A_rename_prepared_before_a_disable_leaves_the_schedule_disabled()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.CreateStandaloneScheduleAsync(ResourceContextIds.Shared, enabled: true);
        await h.Schedules.DisableAsync(schedule.Id);
        h.AuditUnit.Operations.Clear();

        var result = await h.Schedules.PatchScheduleAsync(schedule.Id, new SchedulePatch(Name: "Renamed"));

        var stored = (await h.Schedules.GetDefinitionAsync(schedule.Id))!;
        stored.Enabled.Should().BeFalse("the rename does not carry the enabled flag, so it keeps the disable");
        stored.Name.Should().Be("Renamed");
        result.Should().BeEquivalentTo(stored, "the result is the schedule as committed");
        h.AuditUnit.Actions.Should().Equal(AuditActionNames.ScheduleUpdated);
        h.AuditUnit.Single(AuditActionNames.ScheduleUpdated).Fields["changed"].Should().BeEquivalentTo(new[] { "name" });
    }

    [Fact]
    public async Task A_retime_prepared_before_a_disable_leaves_the_schedule_disabled()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.CreateStandaloneScheduleAsync(ResourceContextIds.Shared, enabled: true);
        await h.Schedules.DisableAsync(schedule.Id);
        h.AuditUnit.Operations.Clear();

        var result = await h.Schedules.PatchScheduleAsync(
            schedule.Id, new SchedulePatch(RecurrenceExpression: "5 4 * * *", TimeZone: "Europe/London"));

        result.Enabled.Should().BeFalse();
        result.RecurrenceExpression.Should().Be("5 4 * * *");
        result.TimeZone.Should().Be("Europe/London");
        (await h.Schedules.GetDefinitionAsync(schedule.Id))!.Enabled.Should().BeFalse();
        h.AuditUnit.Single(AuditActionNames.ScheduleUpdated).Fields["changed"]
            .Should().BeEquivalentTo(new[] { "recurrence", "timeZone" }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task An_explicit_enable_enables_and_is_recorded_as_schedule_enabled()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.CreateStandaloneScheduleAsync(ResourceContextIds.Shared, enabled: false);
        h.AuditUnit.Operations.Clear();

        var result = await h.Schedules.PatchScheduleAsync(schedule.Id, new SchedulePatch(Enabled: true));

        result.Enabled.Should().BeTrue();
        result.Lifecycle!.LastScheduleEnabledAtUtc.Should().NotBeNull();
        (await h.Schedules.GetDefinitionAsync(schedule.Id))!.Enabled.Should().BeTrue();
        h.AuditUnit.Actions.Should().Equal(AuditActionNames.ScheduleEnabled);
        h.AuditUnit.Single(AuditActionNames.ScheduleEnabled).Fields.Should().NotContainKey("changed", "nothing but the flag changed");
    }

    [Fact]
    public async Task An_explicit_disable_disables_and_is_recorded_as_schedule_disabled()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.CreateStandaloneScheduleAsync(ResourceContextIds.Shared, enabled: true);
        h.AuditUnit.Operations.Clear();

        var result = await h.Schedules.PatchScheduleAsync(schedule.Id, new SchedulePatch(Name: "Paused", Enabled: false));

        result.Enabled.Should().BeFalse();
        h.AuditUnit.Actions.Should().Equal(AuditActionNames.ScheduleDisabled);
        h.AuditUnit.Single(AuditActionNames.ScheduleDisabled).Fields["changed"].Should().BeEquivalentTo(new[] { "name" });
    }

    [Fact]
    public async Task An_explicit_enable_under_an_archived_context_is_refused_and_changes_nothing()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var context = await h.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(context.Id, enabled: false);
        await h.ResourceContexts.ArchiveAsync(context.Id);
        h.AuditUnit.Operations.Clear();

        var act = () => h.Schedules.PatchScheduleAsync(schedule.Id, new SchedulePatch(Name: "Renamed", Enabled: true)).AsTask();

        (await act.Should().ThrowAsync<ResourceContextArchivedException>()).WithMessage("*Canvas*archived*");
        var stored = (await h.Schedules.GetDefinitionAsync(schedule.Id))!;
        stored.Enabled.Should().BeFalse();
        stored.Name.Should().Be("Nightly");
        h.AuditUnit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rename_of_a_disabled_schedule_under_an_archived_context_is_allowed()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var context = await h.ResourceContexts.CreateApplicationContextAsync("Canvas");
        var schedule = await h.CreateStandaloneScheduleAsync(context.Id, enabled: false);
        await h.ResourceContexts.ArchiveAsync(context.Id);

        var result = await h.Schedules.PatchScheduleAsync(schedule.Id, new SchedulePatch(Name: "Renamed"));

        result.Name.Should().Be("Renamed");
        result.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Two_patches_of_different_fields_keep_both_changes()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.CreateStandaloneScheduleAsync(ResourceContextIds.Shared, enabled: true);
        h.AuditUnit.Operations.Clear();

        await h.Schedules.PatchScheduleAsync(schedule.Id, new SchedulePatch(Name: "Renamed"));
        var second = await h.Schedules.PatchScheduleAsync(schedule.Id, new SchedulePatch(RecurrenceExpression: "5 4 * * *"));

        second.Name.Should().Be("Renamed", "the second patch did not carry the name, so it keeps the first patch's");
        second.RecurrenceExpression.Should().Be("5 4 * * *");
        second.Enabled.Should().BeTrue();
        var rows = h.AuditUnit.Staged.ToList();
        rows.Should().HaveCount(2);
        rows[0].Fields["changed"].Should().BeEquivalentTo(new[] { "name" });
        rows[1].Fields["changed"].Should().BeEquivalentTo(new[] { "recurrence" });
    }

    [Fact]
    public async Task Two_patches_of_the_same_field_leave_the_last_writers_value()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.CreateStandaloneScheduleAsync(ResourceContextIds.Shared);

        await h.Schedules.PatchScheduleAsync(schedule.Id, new SchedulePatch(Name: "First"));
        await h.Schedules.PatchScheduleAsync(schedule.Id, new SchedulePatch(Name: "Second"));

        (await h.Schedules.GetDefinitionAsync(schedule.Id))!.Name.Should().Be("Second");
    }

    [Fact]
    public async Task Patches_that_change_nothing_record_nothing_and_return_the_stored_schedule()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.CreateStandaloneScheduleAsync(ResourceContextIds.Shared, enabled: true);
        var before = (await h.Schedules.GetDefinitionAsync(schedule.Id))!;
        h.AuditUnit.Operations.Clear();

        var empty = await h.Schedules.PatchScheduleAsync(schedule.Id, new SchedulePatch());
        var same = await h.Schedules.PatchScheduleAsync(
            schedule.Id, new SchedulePatch(before.Name, before.RecurrenceExpression, before.TimeZone, Enabled: true));

        h.AuditUnit.Actions.Should().BeEmpty();
        h.AuditUnit.Operations.Should().HaveCount(2).And.OnlyContain(o => o.NoOp);
        empty.Should().BeEquivalentTo(before);
        same.Should().BeEquivalentTo(before);
        (await h.Schedules.GetDefinitionAsync(schedule.Id))!.Lifecycle.Should().Be(before.Lifecycle);
    }

    [Fact]
    public async Task The_changed_fields_are_those_that_differ_from_the_locked_row_not_those_the_patch_carries()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.CreateStandaloneScheduleAsync(ResourceContextIds.Shared, enabled: true);
        h.AuditUnit.Operations.Clear();

        await h.Schedules.PatchScheduleAsync(
            schedule.Id, new SchedulePatch(Name: schedule.Name, RecurrenceExpression: "5 4 * * *", TimeZone: schedule.TimeZone, Enabled: true));

        h.AuditUnit.Actions.Should().ContainSingle().Which.Should().Be(AuditActionNames.ScheduleUpdated, "the enabled flag did not change");
        h.AuditUnit.Single(AuditActionNames.ScheduleUpdated).Fields["changed"].Should().BeEquivalentTo(new[] { "recurrence" });
    }

    [Fact]
    public async Task A_missing_schedule_is_a_typed_not_found_that_records_nothing()
    {
        await using var h = new ResourceContextOwnershipTestHarness();

        var act = () => h.Schedules.PatchScheduleAsync(new ScheduleId("sch-missing"), new SchedulePatch(Name: "x")).AsTask();

        await act.Should().ThrowAsync<ScheduleNotFoundException>();
        h.AuditUnit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_lost_acknowledgement_returns_the_committed_schedule_and_runs_the_attempt_once()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.CreateStandaloneScheduleAsync(ResourceContextIds.Shared, enabled: false);
        h.AuditUnit.Operations.Clear();
        h.AuditUnit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var result = await h.Schedules.PatchScheduleAsync(schedule.Id, new SchedulePatch(Name: "Renamed"));

        result.Name.Should().Be("Renamed");
        result.Enabled.Should().BeFalse();
        h.AuditUnit.Operations.Should().ContainSingle();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Only_an_explicit_enable_takes_the_ownership_gate_whatever_the_stored_flag(bool? enabled, bool takesGate)
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.CreateStandaloneScheduleAsync(ResourceContextIds.Shared, enabled: true);
        var gate = new Mock<IResourceOwnershipMutationGate>();
        var service = new SqlScheduleManagementService(
            h.ScheduleRepository,
            Mock.Of<ISchedulerClaimOptions>(),
            TimeProvider.System,
            h.ResourceContexts,
            h.IntegrationRepository,
            h.Db,
            gate.Object,
            ActingPrincipal(),
            new RecordingAuditedUnitOfWork());

        await service.PatchScheduleAsync(schedule.Id, new SchedulePatch(Name: "Renamed", Enabled: enabled));

        gate.Verify(g => g.AcquireAsync(It.IsAny<CancellationToken>()), takesGate ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task A_direct_update_still_applies_the_enabled_flag_its_definition_carries()
    {
        // UpdateScheduleAsync keeps its established semantics for direct callers: the definition's four fields are applied.
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.CreateStandaloneScheduleAsync(ResourceContextIds.Shared, enabled: true);
        await h.Schedules.DisableAsync(schedule.Id);

        await h.Schedules.UpdateScheduleAsync(schedule with { Name = "Renamed" });

        (await h.Schedules.GetDefinitionAsync(schedule.Id))!.Enabled.Should().BeTrue();
    }

    [Fact]
    public void A_patch_applies_only_the_fields_it_carries()
    {
        var current = new ScheduleDefinition(
            new ScheduleId("s1"), "Nightly", "0 0 * * *", "UTC", Enabled: false,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"), "policy", Version: 3);

        new SchedulePatch().ApplyTo(current).Should().Be(current);
        new SchedulePatch(Name: "Renamed").ApplyTo(current).Should().Be(current with { Name = "Renamed" });
        new SchedulePatch(Enabled: true).ApplyTo(current).Should().Be(current with { Enabled = true });
        new SchedulePatch(RecurrenceExpression: "5 4 * * *", TimeZone: "Europe/London").ApplyTo(current)
            .Should().Be(current with { RecurrenceExpression = "5 4 * * *", TimeZone = "Europe/London" });
    }

    private static IPrincipalContext ActingPrincipal()
    {
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IActor>());
        return principal.Object;
    }
}
