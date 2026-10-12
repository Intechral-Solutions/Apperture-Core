using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using Apperture.UnitTests.ResourceContexts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Scheduling;

/// <summary>
/// AUD-1.4 slice 4: which audit rows the schedule create (with its initial assignment), update, enable, disable and standalone
/// assignment record, and what they refuse to change without a unit (M7 at the service; the transactional guarantees are proven
/// against SQL Server in <c>ScheduleLifecycleAuditSqlServerTests</c>).
/// </summary>
public sealed class ScheduleLifecycleAuditTests
{
    private const string Recurrence = "0 3 * * *";

    private static ScheduleDefinition NewDefinition(IntegrationId? integrationId, bool enabled = false, string name = "Nightly import") =>
        new(
            new ScheduleId("sch-" + Guid.NewGuid().ToString("N")[..12]),
            name,
            Recurrence,
            "UTC",
            enabled,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{\"parameter\":\"sentinel-target\"}"),
            "policy-ref-sentinel",
            Version: 1,
            integrationId);

    [Fact]
    public async Task A_creation_with_its_assignment_records_one_schedule_created_row_and_stores_both_together()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var integration = await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Payments", "Sample.Mod"));
        h.AuditUnit.Operations.Clear();

        var created = await h.Schedules.CreateScheduleWithAssignmentAsync(NewDefinition(integration.Id), new RuntimePlaneId("local"), null);

        h.AuditUnit.Actions.Should().Equal(AuditActionNames.ScheduleCreated);
        var evt = h.AuditUnit.Single(AuditActionNames.ScheduleCreated);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.Schedule, created.Id.Value));
        evt.Related.ScheduleId.Should().Be(created.Id.Value);
        evt.Related.ResourceContextId.Should().Be(ResourceContextIds.SharedValue, "inherited from the integration");
        evt.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["name"] = "Nightly import",
            ["integrationId"] = integration.Id.Value,
            ["enabled"] = false,
            ["runtimePlaneId"] = "local",
            ["assignmentsCreated"] = 1,
        });
        System.Text.Json.JsonSerializer.Serialize(evt.Fields).Should().NotContain(Recurrence).And.NotContain("sentinel");
        (await h.Db.ScheduleDefinitions.CountAsync()).Should().Be(1);
        (await h.Db.ScheduleAssignments.CountAsync(a => a.ScheduleId == created.Id.Value && a.RuntimePlaneId == "local")).Should().Be(1);
    }

    [Fact]
    public async Task A_creation_without_an_assignment_records_zero_assignments()
    {
        await using var h = new ResourceContextOwnershipTestHarness();

        var created = await h.Schedules.CreateScheduleAsync(NewDefinition(integrationId: null));

        var evt = h.AuditUnit.Single(AuditActionNames.ScheduleCreated);
        evt.Fields["assignmentsCreated"].Should().Be(0);
        evt.Fields.Should().NotContainKey("runtimePlaneId").And.NotContainKey("integrationId");
        (await h.Db.ScheduleAssignments.CountAsync(a => a.ScheduleId == created.Id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task A_refused_creation_for_an_unknown_integration_records_nothing_and_stores_nothing()
    {
        await using var h = new ResourceContextOwnershipTestHarness();

        var act = () => h.Schedules.CreateScheduleWithAssignmentAsync(
            NewDefinition(new IntegrationId("int-missing")), new RuntimePlaneId("local"), null).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        h.AuditUnit.Actions.Should().BeEmpty();
        (await h.Db.ScheduleDefinitions.CountAsync()).Should().Be(0);
        (await h.Db.ScheduleAssignments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Renaming_and_retiming_record_schedule_updated_with_field_names_and_no_values()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.Schedules.CreateScheduleAsync(NewDefinition(null));
        h.AuditUnit.Operations.Clear();

        await h.Schedules.UpdateScheduleAsync(schedule with { Name = "Renamed", RecurrenceExpression = "5 4 * * *", TimeZone = "Europe/London" });

        h.AuditUnit.Actions.Should().Equal(AuditActionNames.ScheduleUpdated);
        var evt = h.AuditUnit.Single(AuditActionNames.ScheduleUpdated);
        evt.Fields["changed"].Should().BeEquivalentTo(new[] { "name", "recurrence", "timeZone" }, o => o.WithStrictOrdering());
        evt.Fields["name"].Should().Be("Renamed");
        System.Text.Json.JsonSerializer.Serialize(evt.Fields).Should().NotContain("5 4 * * *").And.NotContain("London");
        var stored = await h.Schedules.GetDefinitionAsync(schedule.Id);
        stored!.RecurrenceExpression.Should().Be("5 4 * * *");
    }

    [Fact]
    public async Task An_update_applies_only_the_patchable_fields_over_the_stored_schedule()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.Schedules.CreateScheduleAsync(NewDefinition(null));

        await h.Schedules.UpdateScheduleAsync(schedule with
        {
            Name = "Renamed",
            ExecutionPolicyRef = "somebody-elses-policy",
            Target = new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{\"different\":true}"),
        });

        var stored = await h.Schedules.GetDefinitionAsync(schedule.Id);
        stored!.Name.Should().Be("Renamed");
        stored.ExecutionPolicyRef.Should().Be("policy-ref-sentinel", "an update never rewrites the execution policy");
        stored.Target.JobConfiguration.Should().Contain("sentinel-target", "the target is immutable after creation");
    }

    [Fact]
    public async Task Enabling_and_disabling_are_recorded_under_their_own_actions_and_update_the_lifecycle_timestamps()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.Schedules.CreateScheduleAsync(NewDefinition(null));
        h.AuditUnit.Operations.Clear();

        await h.Schedules.EnableAsync(schedule.Id);
        var enabled = await h.Schedules.GetDefinitionAsync(schedule.Id);
        await h.Schedules.DisableAsync(schedule.Id);
        var disabled = await h.Schedules.GetDefinitionAsync(schedule.Id);

        h.AuditUnit.Actions.Should().Equal(AuditActionNames.ScheduleEnabled, AuditActionNames.ScheduleDisabled);
        enabled!.Enabled.Should().BeTrue();
        enabled.Lifecycle!.LastScheduleEnabledAtUtc.Should().NotBeNull();
        disabled!.Enabled.Should().BeFalse();
        disabled.Lifecycle!.LastScheduleDisabledAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task A_request_that_enables_and_renames_records_one_enabled_row_naming_the_other_change()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.Schedules.CreateScheduleAsync(NewDefinition(null));
        h.AuditUnit.Operations.Clear();

        await h.Schedules.UpdateScheduleAsync(schedule with { Name = "Renamed", Enabled = true });

        h.AuditUnit.Actions.Should().Equal(AuditActionNames.ScheduleEnabled);
        h.AuditUnit.Single(AuditActionNames.ScheduleEnabled).Fields["changed"].Should().BeEquivalentTo(new[] { "name" });
    }

    [Fact]
    public async Task Requests_that_change_nothing_record_nothing_and_leave_the_lifecycle_timestamps_alone()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.Schedules.CreateScheduleAsync(NewDefinition(null, enabled: true));
        var before = await h.Schedules.GetDefinitionAsync(schedule.Id);
        h.AuditUnit.Operations.Clear();

        await h.Schedules.EnableAsync(schedule.Id);
        await h.Schedules.UpdateScheduleAsync(schedule);
        await h.Schedules.UpdateScheduleAsync(schedule with { Enabled = true });

        h.AuditUnit.Actions.Should().BeEmpty();
        h.AuditUnit.Operations.Should().HaveCount(3).And.OnlyContain(o => o.NoOp);
        (await h.Schedules.GetDefinitionAsync(schedule.Id))!.Lifecycle.Should().Be(before!.Lifecycle);
    }

    [Fact]
    public async Task A_missing_schedule_is_a_typed_not_found_that_records_nothing_for_every_change()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var missing = new ScheduleId("sch-missing");

        var enable = () => h.Schedules.EnableAsync(missing).AsTask();
        var disable = () => h.Schedules.DisableAsync(missing).AsTask();
        var update = () => h.Schedules.UpdateScheduleAsync(NewDefinition(null) with { Id = missing }).AsTask();
        var assign = () => h.Schedules.AssignToRuntimePlaneAsync(missing, new RuntimePlaneId("local"), null).AsTask();

        await enable.Should().ThrowAsync<ScheduleNotFoundException>();
        await disable.Should().ThrowAsync<ScheduleNotFoundException>();
        await update.Should().ThrowAsync<ScheduleNotFoundException>();
        await assign.Should().ThrowAsync<ScheduleNotFoundException>();
        h.AuditUnit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_standalone_assignment_is_audited_and_never_records_the_policy_overrides()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var schedule = await h.Schedules.CreateScheduleAsync(NewDefinition(null));
        h.AuditUnit.Operations.Clear();

        var assignment = await h.Schedules.AssignToRuntimePlaneAsync(schedule.Id, new RuntimePlaneId("plane-2"), "{\"secret\":\"sentinel-overrides\"}");

        assignment.RuntimePlaneId.Value.Should().Be("plane-2");
        var evt = h.AuditUnit.Single(AuditActionNames.ScheduleAssigned);
        evt.Fields.Should().BeEquivalentTo(new Dictionary<string, object?> { ["runtimePlaneId"] = "plane-2", ["policyOverridesSet"] = true });
        System.Text.Json.JsonSerializer.Serialize(evt.Fields).Should().NotContain("sentinel");
    }

    [Fact]
    public async Task A_lost_acknowledgement_returns_the_committed_schedule_and_runs_the_attempt_once()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        h.AuditUnit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var created = await h.Schedules.CreateScheduleWithAssignmentAsync(NewDefinition(null), new RuntimePlaneId("local"), null);

        created.Name.Should().Be("Nightly import");
        h.AuditUnit.Operations.Should().ContainSingle();
    }

    [Fact]
    public async Task An_unknown_outcome_is_reported_as_unknown()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        h.AuditUnit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        var act = () => h.Schedules.CreateScheduleWithAssignmentAsync(NewDefinition(null), new RuntimePlaneId("local"), null).AsTask();

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
    }

    [Fact]
    public async Task Every_mutation_refuses_to_run_without_an_actor_and_without_the_unit()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var existing = await h.Schedules.CreateScheduleAsync(NewDefinition(null));
        var withActor = new Mock<IPrincipalContext>();
        withActor.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IActor>());
        var noUnit = new SqlScheduleManagementService(
            h.ScheduleRepository,
            Mock.Of<ISchedulerClaimOptions>(),
            TimeProvider.System,
            h.ResourceContexts,
            h.IntegrationRepository,
            h.Db,
            Mock.Of<Apperture.Core.Application.ResourceContexts.IResourceOwnershipMutationGate>(),
            withActor.Object);
        var noActor = new SqlScheduleManagementService(
            h.ScheduleRepository,
            Mock.Of<ISchedulerClaimOptions>(),
            TimeProvider.System,
            h.ResourceContexts,
            h.IntegrationRepository,
            h.Db,
            Mock.Of<Apperture.Core.Application.ResourceContexts.IResourceOwnershipMutationGate>(),
            Mock.Of<IPrincipalContext>(),
            new RecordingAuditedUnitOfWork());

        foreach (var service in new[] { noUnit })
        {
            await FluentActions.Awaiting(() => service.CreateScheduleAsync(NewDefinition(null)).AsTask()).Should().ThrowAsync<AuditInvariantException>();
            await FluentActions.Awaiting(() => service.EnableAsync(existing.Id).AsTask()).Should().ThrowAsync<AuditInvariantException>();
            await FluentActions.Awaiting(() => service.DisableAsync(existing.Id).AsTask()).Should().ThrowAsync<AuditInvariantException>();
            await FluentActions.Awaiting(() => service.UpdateScheduleAsync(existing with { Name = "x" }).AsTask()).Should().ThrowAsync<AuditInvariantException>();
            await FluentActions.Awaiting(() => service.AssignToRuntimePlaneAsync(existing.Id, new RuntimePlaneId("p"), null).AsTask()).Should().ThrowAsync<AuditInvariantException>();
        }

        await FluentActions.Awaiting(() => noActor.CreateScheduleAsync(NewDefinition(null)).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => noActor.EnableAsync(existing.Id).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        (await h.Db.ScheduleDefinitions.CountAsync()).Should().Be(1, "nothing was changed unaudited or anonymously");
    }
}
