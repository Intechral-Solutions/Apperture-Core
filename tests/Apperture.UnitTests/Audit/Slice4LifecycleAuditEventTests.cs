using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.4 slice 4: the catalog entries, detail schemas and typed producers of the integration, binding and schedule lifecycle
/// (design gate §11, §12, §13.1; M7). The transactional behaviour is proven by the service tests and the SQL Server suites.
/// </summary>
public sealed class Slice4LifecycleAuditEventTests
{
    private static readonly string[] AllActions =
    [
        AuditActionNames.IntegrationCreated, AuditActionNames.IntegrationUpdated, AuditActionNames.IntegrationActivated,
        AuditActionNames.IntegrationDeactivated, AuditActionNames.IntegrationRetired, AuditActionNames.BindingCreated,
        AuditActionNames.BindingDeleted, AuditActionNames.ScheduleCreated, AuditActionNames.ScheduleUpdated,
        AuditActionNames.ScheduleEnabled, AuditActionNames.ScheduleDisabled, AuditActionNames.ScheduleAssigned,
    ];

    [Fact]
    public void Every_action_is_a_required_configuration_change_of_standard_severity_with_a_schema()
    {
        foreach (var action in AllActions)
        {
            var definition = AuditActionCatalog.Default.Get(action);
            definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation, action);
            definition.Basis.Should().Be(AuditBases.Change, action);
            definition.Category.Should().Be(AuditCategories.Configuration, action);
            definition.Severity.Should().Be(AuditSeverities.Standard, action);
            definition.Details.Should().NotBeNull(action);
        }
    }

    [Fact]
    public void Each_action_targets_its_own_entity_and_binding_is_part_of_the_closed_vocabulary()
    {
        AuditTargetTypes.All.Should().Contain(AuditTargetTypes.Binding);
        AuditTargetTypes.Binding.Should().Be("binding");
        foreach (var action in AllActions.Where(a => a.StartsWith("integration.", StringComparison.Ordinal)))
            AuditActionCatalog.Default.Get(action).TargetType.Should().Be(AuditTargetTypes.Integration, action);
        foreach (var action in AllActions.Where(a => a.StartsWith("binding.", StringComparison.Ordinal)))
            AuditActionCatalog.Default.Get(action).TargetType.Should().Be(AuditTargetTypes.Binding, action);
        foreach (var action in AllActions.Where(a => a.StartsWith("schedule.", StringComparison.Ordinal)))
            AuditActionCatalog.Default.Get(action).TargetType.Should().Be(AuditTargetTypes.Schedule, action);
    }

    [Fact]
    public void No_event_can_carry_a_value_a_recurrence_a_secret_or_free_text()
    {
        foreach (var action in AllActions)
        {
            var fields = AuditActionCatalog.Default.Get(action).Details!.Fields;
            fields.Should().OnlyContain(
                f => f.Kind == AuditFieldKind.NameSnapshot || f.Kind == AuditFieldKind.Id || f.Kind == AuditFieldKind.Count
                    || f.Kind == AuditFieldKind.Enum || f.Kind == AuditFieldKind.Flag || f.Kind == AuditFieldKind.IdList,
                action);
            fields.Select(f => f.Name).Should().NotContain(n =>
                n.Contains("recurrence", StringComparison.OrdinalIgnoreCase) && !n.Equals("changed", StringComparison.Ordinal)
                || n.Contains("cron", StringComparison.OrdinalIgnoreCase)
                || n.Contains("timeZone", StringComparison.OrdinalIgnoreCase)
                || n.Contains("target", StringComparison.OrdinalIgnoreCase)
                || n.Contains("policy", StringComparison.OrdinalIgnoreCase) && !n.Equals("policyOverridesSet", StringComparison.Ordinal)
                || n.Contains("setting", StringComparison.OrdinalIgnoreCase) && !n.Equals("settingsProvided", StringComparison.Ordinal)
                || n.Contains("contract", StringComparison.OrdinalIgnoreCase),
                action);
        }
    }

    [Fact]
    public void An_integration_creation_records_its_name_module_and_context_but_never_the_settings()
    {
        var created = AuditEvents.IntegrationCreated("int-1", "Sample API Polling", "Sample.ApiPolling", settingsProvided: true, resourceContextId: "ctx-1");

        created.Action.Should().Be("integration.created");
        created.Target.Should().Be(new AuditTarget(AuditTargetTypes.Integration, "int-1"));
        created.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        created.Related.ModuleId.Should().Be("Sample.ApiPolling");
        created.Related.ResourceContextId.Should().Be("ctx-1");
        created.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["name"] = "Sample API Polling",
            ["moduleId"] = "Sample.ApiPolling",
            ["settingsProvided"] = true,
        });
    }

    [Fact]
    public void An_integration_update_names_the_changed_fields_in_declared_order_and_is_never_empty()
    {
        var updated = AuditEvents.IntegrationUpdated("int-1", "Sample", "Sample.Mod", ["settings", "displayName"], "ctx-1");

        updated.Action.Should().Be("integration.updated");
        updated.Fields["changed"].Should().BeEquivalentTo(new[] { "displayName", "settings" }, o => o.WithStrictOrdering());

        var empty = () => AuditEvents.IntegrationUpdated("int-1", "Sample", null, [], null);
        var unknown = () => AuditEvents.IntegrationUpdated("int-1", "Sample", null, ["state"], null);
        empty.Should().Throw<ArgumentException>("an update that changes nothing is not recorded");
        unknown.Should().Throw<ArgumentException>("only declared field names may be listed");
    }

    [Theory]
    [InlineData(IntegrationState.Active, IntegrationState.Inactive, "integration.deactivated", "active", "inactive")]
    [InlineData(IntegrationState.Inactive, IntegrationState.Active, "integration.activated", "inactive", "active")]
    [InlineData(IntegrationState.Retired, IntegrationState.Active, "integration.activated", "retired", "active")]
    [InlineData(IntegrationState.Active, IntegrationState.Retired, "integration.retired", "active", "retired")]
    [InlineData(IntegrationState.Inactive, IntegrationState.Retired, "integration.retired", "inactive", "retired")]
    public void A_state_transition_is_named_by_its_action_and_records_both_states(
        IntegrationState from, IntegrationState to, string action, string fromName, string toName)
    {
        var changed = AuditEvents.IntegrationStateChanged(action, "int-1", "Sample", "Sample.Mod", from, to, [], "ctx-1");

        changed.Action.Should().Be(action);
        changed.Fields["fromState"].Should().Be(fromName);
        changed.Fields["toState"].Should().Be(toName);
        changed.Fields.Should().NotContainKey("changed", "no other field changed");
    }

    [Fact]
    public void A_state_transition_with_other_changes_lists_their_names_and_a_mismatched_action_is_refused()
    {
        var changed = AuditEvents.IntegrationStateChanged(
            AuditActionNames.IntegrationActivated, "int-1", "Sample", null, IntegrationState.Inactive, IntegrationState.Active, ["displayName"], null);
        changed.Fields["changed"].Should().BeEquivalentTo(new[] { "displayName" });

        var wrongAction = () => AuditEvents.IntegrationStateChanged(
            AuditActionNames.IntegrationRetired, "int-1", "Sample", null, IntegrationState.Inactive, IntegrationState.Active, [], null);
        var unchanged = () => AuditEvents.IntegrationStateChanged(
            AuditActionNames.IntegrationActivated, "int-1", "Sample", null, IntegrationState.Active, IntegrationState.Active, [], null);
        var notATransition = () => AuditEvents.IntegrationStateChanged(
            AuditActionNames.IntegrationUpdated, "int-1", "Sample", null, IntegrationState.Inactive, IntegrationState.Active, [], null);
        wrongAction.Should().Throw<ArgumentException>();
        unchanged.Should().Throw<ArgumentException>("a request that leaves the state as it was is not a transition");
        notATransition.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_binding_event_names_the_binding_its_module_slot_and_what_it_binds_and_keeps_the_context()
    {
        var created = AuditEvents.BindingCreated("bnd-1", "int-1", "Sample.Mod", "source", "conn-1", capabilityId: null, resourceContextId: "ctx-1");
        var deleted = AuditEvents.BindingDeleted("bnd-1", "int-1", "Sample.Mod", "source", "conn-1", null, "ctx-1");

        created.Action.Should().Be("binding.created");
        deleted.Action.Should().Be("binding.deleted");
        created.Target.Should().Be(new AuditTarget(AuditTargetTypes.Binding, "bnd-1"));
        created.Related.ModuleId.Should().Be("Sample.Mod");
        created.Related.ResourceContextId.Should().Be("ctx-1");
        created.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["moduleId"] = "Sample.Mod",
            ["slotName"] = "source",
            ["integrationId"] = "int-1",
            ["connectionId"] = "conn-1",
        });
        deleted.Fields.Should().BeEquivalentTo(created.Fields);
    }

    [Fact]
    public void A_contextless_capability_binding_records_its_capability_and_no_integration()
    {
        var created = AuditEvents.BindingCreated("bnd-2", null, "Sample.Mod", "source", null, "capability-1", null);

        created.Fields.Should().NotContainKey("integrationId").And.NotContainKey("connectionId");
        created.Fields["capabilityId"].Should().Be("capability-1");
        created.Related.ResourceContextId.Should().BeNull();
    }

    [Fact]
    public void A_schedule_creation_records_the_initial_assignment_but_not_the_recurrence_or_target()
    {
        var created = AuditEvents.ScheduleCreated("sch-1", "Nightly import", "int-1", enabled: true, runtimePlaneId: "local", assignmentsCreated: 1, resourceContextId: "ctx-1");

        created.Action.Should().Be("schedule.created");
        created.Target.Should().Be(new AuditTarget(AuditTargetTypes.Schedule, "sch-1"));
        created.Related.ScheduleId.Should().Be("sch-1");
        created.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["name"] = "Nightly import",
            ["integrationId"] = "int-1",
            ["enabled"] = true,
            ["runtimePlaneId"] = "local",
            ["assignmentsCreated"] = 1,
        });
    }

    [Fact]
    public void A_schedule_update_names_changed_fields_not_values_and_the_enabled_flag_has_its_own_actions()
    {
        var updated = AuditEvents.ScheduleUpdated("sch-1", "Nightly", "int-1", ["timeZone", "recurrence"], "ctx-1");
        updated.Fields["changed"].Should().BeEquivalentTo(new[] { "recurrence", "timeZone" }, o => o.WithStrictOrdering());

        AuditEvents.ScheduleEnabledChanged(true, "sch-1", "Nightly", null, [], null).Action.Should().Be("schedule.enabled");
        var disabled = AuditEvents.ScheduleEnabledChanged(false, "sch-1", "Nightly", null, ["name"], null);
        disabled.Action.Should().Be("schedule.disabled");
        disabled.Fields["changed"].Should().BeEquivalentTo(new[] { "name" });

        var empty = () => AuditEvents.ScheduleUpdated("sch-1", "Nightly", null, [], null);
        var enabledFlag = () => AuditEvents.ScheduleUpdated("sch-1", "Nightly", null, ["enabled"], null);
        empty.Should().Throw<ArgumentException>();
        enabledFlag.Should().Throw<ArgumentException>("the enabled flag is recorded by schedule.enabled and schedule.disabled");
    }

    [Fact]
    public void A_standalone_assignment_records_the_plane_and_whether_overrides_were_set_but_never_their_content()
    {
        var assigned = AuditEvents.ScheduleAssigned("sch-1", "local", policyOverridesSet: true, resourceContextId: "ctx-1");

        assigned.Action.Should().Be("schedule.assigned");
        assigned.Fields.Should().BeEquivalentTo(new Dictionary<string, object?> { ["runtimePlaneId"] = "local", ["policyOverridesSet"] = true });
    }

    [Fact]
    public void The_schemas_refuse_an_undeclared_field_and_a_secret_looking_value_has_nowhere_to_go()
    {
        var schema = AuditActionCatalog.Default.Get(AuditActionNames.ScheduleUpdated).Details!;
        var values = new Dictionary<string, object?> { ["name"] = "Nightly", ["changed"] = new[] { "name" }.ToList(), ["recurrenceExpression"] = "0 0 * * *" };

        var act = () => schema.Validate(AuditActionNames.ScheduleUpdated, values);

        act.Should().Throw<ArgumentException>();
        AuditActionCatalog.Default.Get(AuditActionNames.IntegrationCreated).Details!.Fields.Select(f => f.Name)
            .Should().NotContain(n => n == "settings" || n == "scopeKey");
    }

    [Fact]
    public void The_longest_stored_values_fit_without_truncating_anything_that_is_not_a_name()
    {
        var name = new string('n', 256);
        var created = AuditEvents.ScheduleCreated(new string('s', 64), name, new string('i', 64), true, new string('p', 128), 1, new string('c', 64));
        var binding = AuditEvents.BindingCreated(new string('b', 64), new string('i', 64), new string('m', 128), new string('l', 128), new string('c', 64), new string('a', 128), new string('x', 64));

        foreach (var evt in new[] { created, binding })
        {
            var details = evt.Definition.Details!.Serialize(evt.Action, evt.Definition.WriteClass, evt.Fields);
            details.Truncated.Should().BeEmpty(evt.Action);
            details.Json.Length.Should().BeLessThan(32 * 1024);
        }
    }

    [Fact]
    public void The_producers_refuse_what_the_schema_cannot_hold()
    {
        var negative = () => AuditEvents.ScheduleCreated("sch-1", "Nightly", null, true, null, -1, null);
        var longName = () => AuditEvents.IntegrationCreated("int-1", new string('n', 257), null, false, null);

        negative.Should().Throw<ArgumentOutOfRangeException>();
        longName.Should().Throw<ArgumentException>("an integration name is at most 256 characters in the database");
    }
}
