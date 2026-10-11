using Apperture.Core.Application.Audit;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.4 slice 3: the catalog entries, detail schemas and typed producers of the schedule hard-delete (design gate §11, §12,
/// §13.1; M7). The transactional behaviour is proven by <c>ScheduleDeleteTests</c> and <c>ScheduleDeleteAuditSqlServerTests</c>.
/// </summary>
public sealed class ScheduleDeleteAuditEventTests
{
    private const string ScheduleId = "sch-0123456789ab";

    [Fact]
    public void The_deletion_is_a_required_configuration_change_with_a_schema_and_the_schedule_as_its_target()
    {
        var definition = AuditActionCatalog.Default.Get(AuditActionNames.ScheduleDeleted);

        definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation);
        definition.Basis.Should().Be(AuditBases.Change);
        definition.Category.Should().Be(AuditCategories.Configuration);
        definition.Severity.Should().Be(AuditSeverities.Standard);
        definition.TargetType.Should().Be(AuditTargetTypes.Schedule);
        definition.Details.Should().NotBeNull();
        definition.Details!.Fields.Select(f => f.Name).Should().BeEquivalentTo("name", "integrationId", "assignmentsRemoved");
    }

    [Fact]
    public void The_refusal_is_a_best_effort_attempt_with_a_schema_and_a_closed_reason()
    {
        var definition = AuditActionCatalog.Default.Get(AuditActionNames.ScheduleDeleteRejected);

        definition.WriteClass.Should().Be(AuditWriteClass.Attempt);
        definition.Basis.Should().Be(AuditBases.Attempt);
        definition.Category.Should().Be(AuditCategories.Configuration);
        definition.TargetType.Should().Be(AuditTargetTypes.Schedule);
        definition.Details!.Fields.Select(f => f.Name).Should().BeEquivalentTo("name", "integrationId", "reason");
        definition.Details.Fields.Single(f => f.Name == "reason").EnumValues.Should().BeEquivalentTo("enabled");
    }

    [Fact]
    public void Neither_event_can_carry_the_recurrence_the_target_configuration_or_free_text()
    {
        foreach (var action in new[] { AuditActionNames.ScheduleDeleted, AuditActionNames.ScheduleDeleteRejected })
        {
            var fields = AuditActionCatalog.Default.Get(action).Details!.Fields;
            fields.Should().OnlyContain(
                f => f.Kind == AuditFieldKind.NameSnapshot || f.Kind == AuditFieldKind.Id || f.Kind == AuditFieldKind.Count || f.Kind == AuditFieldKind.Enum,
                action);
            fields.Select(f => f.Name).Should().NotContain(n => n.Contains("recurrence", StringComparison.OrdinalIgnoreCase)
                || n.Contains("target", StringComparison.OrdinalIgnoreCase)
                || n.Contains("cron", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void A_deletion_names_the_schedule_as_target_and_related_reference_and_keeps_its_context()
    {
        var deleted = AuditEvents.ScheduleDeleted(ScheduleId, "Nightly", "int-1", assignmentsRemoved: 2, resourceContextId: "ctx-1");

        deleted.Action.Should().Be("schedule.deleted");
        deleted.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        deleted.Target.Should().Be(new AuditTarget(AuditTargetTypes.Schedule, ScheduleId));
        deleted.Related.ScheduleId.Should().Be(ScheduleId);
        deleted.Related.ResourceContextId.Should().Be("ctx-1");
        deleted.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["name"] = "Nightly",
            ["integrationId"] = "int-1",
            ["assignmentsRemoved"] = 2,
        });
    }

    [Fact]
    public void A_standalone_schedule_records_no_integration_and_zero_assignments_is_a_value()
    {
        var deleted = AuditEvents.ScheduleDeleted(ScheduleId, "Nightly", integrationId: null, assignmentsRemoved: 0, resourceContextId: null);

        deleted.Fields.Should().NotContainKey("integrationId");
        deleted.Fields["assignmentsRemoved"].Should().Be(0);
        deleted.Related.ResourceContextId.Should().BeNull();
    }

    [Fact]
    public void A_refusal_is_rejected_and_names_the_reason_without_an_assignment_count()
    {
        var rejected = AuditEvents.ScheduleDeleteRejected(ScheduleId, "Nightly", "int-1", "ctx-1");

        rejected.Action.Should().Be("schedule.delete_rejected");
        rejected.OutcomeCode.Should().Be(AuditOutcomeCodes.Rejected);
        rejected.Target.Should().Be(new AuditTarget(AuditTargetTypes.Schedule, ScheduleId));
        rejected.Related.ScheduleId.Should().Be(ScheduleId);
        rejected.Related.ResourceContextId.Should().Be("ctx-1");
        rejected.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["name"] = "Nightly",
            ["integrationId"] = "int-1",
            ["reason"] = "enabled",
        });
    }

    [Fact]
    public void The_producers_refuse_what_the_schema_cannot_hold()
    {
        var negative = () => AuditEvents.ScheduleDeleted(ScheduleId, "Nightly", null, assignmentsRemoved: -1, resourceContextId: null);
        var unknownReason = () => AuditEvents.ScheduleDeleteRejected(ScheduleId, "Nightly", null, null, reason: "because");
        var nameLongerThanStored = () => AuditEvents.ScheduleDeleted(ScheduleId, new string('n', 257), null, 0, null);

        negative.Should().Throw<ArgumentOutOfRangeException>();
        unknownReason.Should().Throw<ArgumentException>();
        nameLongerThanStored.Should().Throw<ArgumentException>("a schedule name is at most 256 characters in the database");
    }

    [Fact]
    public void The_longest_stored_values_fit_without_truncating_anything_that_is_not_a_name()
    {
        var name = new string('n', 256);
        var scheduleId = new string('s', 64);
        var integrationId = new string('i', 64);
        var contextId = new string('c', 64);

        var deleted = AuditEvents.ScheduleDeleted(scheduleId, name, integrationId, assignmentsRemoved: int.MaxValue, resourceContextId: contextId);
        var details = deleted.Definition.Details!.Serialize(deleted.Action, deleted.Definition.WriteClass, deleted.Fields);

        details.Json.Length.Should().BeLessThan(32 * 1024);
        deleted.Related.ScheduleId.Should().Be(scheduleId);
        deleted.Related.ResourceContextId.Should().Be(contextId);
    }

    [Fact]
    public void A_context_id_the_column_cannot_hold_is_left_out_of_the_related_reference_instead_of_failing_the_row()
    {
        var deleted = AuditEvents.ScheduleDeleted(ScheduleId, "Nightly", null, 0, resourceContextId: new string('c', 65));

        deleted.Related.ResourceContextId.Should().BeNull();
    }
}
