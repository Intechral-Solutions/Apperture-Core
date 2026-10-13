using System.Text.Json;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.4 slice 7: the catalog entries, detail schemas and typed producers of the attention routing, module lifecycle and runtime
/// shutdown request families (design gate §11, §12, §13.1; M7). The transactional behaviour is proven by the service tests and the
/// SQL Server suites.
/// </summary>
public sealed class Slice7AuditEventTests
{
    private const string TextSentinel = "someone.private@example.com injected text";
    private const string MessageSentinel = "Setting 'Password' of module other-module conflicts with this one";

    private static readonly string[] RoutingActions =
    [
        AuditActionNames.AttentionRoutingDefaultUpdated, AuditActionNames.AttentionRoutingOverrideCreated,
        AuditActionNames.AttentionRoutingOverrideUpdated, AuditActionNames.AttentionRoutingOverrideCleared,
    ];

    private static readonly string[] ModuleChangeActions =
    [
        AuditActionNames.ModuleInstalled, AuditActionNames.ModuleEnabled, AuditActionNames.ModuleDisabled, AuditActionNames.ModuleUninstalled,
    ];

    private static readonly string[] ModuleBlockedActions =
    [
        AuditActionNames.ModuleInstallBlocked, AuditActionNames.ModuleEnableBlocked,
        AuditActionNames.ModuleDisableBlocked, AuditActionNames.ModuleUninstallBlocked,
    ];

    // ---- Catalog ----

    [Fact]
    public void Every_action_has_a_structured_schema()
    {
        foreach (var action in RoutingActions.Concat(ModuleChangeActions).Concat(ModuleBlockedActions).Append(AuditActionNames.RuntimeShutdownRequestCreated))
            AuditActionCatalog.Default.Get(action).Details.Should().NotBeNull(action);
    }

    [Fact]
    public void The_routing_and_module_change_writers_are_required_mutations_that_commit_with_their_change()
    {
        foreach (var action in RoutingActions.Concat(ModuleChangeActions))
        {
            var definition = AuditActionCatalog.Default.Get(action);
            definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation, action);
            definition.Basis.Should().Be(AuditBases.Change, action);
            definition.Category.Should().Be(AuditCategories.Configuration, action);
            definition.Severity.Should().Be(AuditSeverities.Standard, action);
        }
    }

    [Fact]
    public void A_blocked_module_attempt_is_a_w3_attempt_row_of_the_module()
    {
        foreach (var action in ModuleBlockedActions)
        {
            var definition = AuditActionCatalog.Default.Get(action);
            definition.WriteClass.Should().Be(AuditWriteClass.Attempt, action);
            definition.Basis.Should().Be(AuditBases.Attempt, action);
            definition.TargetType.Should().Be(AuditTargetTypes.Module, action);
            definition.Category.Should().Be(AuditCategories.Configuration, action);
            definition.IntroducedAfterEpic37Migration.Should().BeTrue(action);
        }
    }

    [Fact]
    public void Each_routing_action_targets_the_instance_or_the_integration()
    {
        AuditActionCatalog.Default.Get(AuditActionNames.AttentionRoutingDefaultUpdated).TargetType.Should().Be(AuditTargetTypes.Instance);
        foreach (var action in RoutingActions.Skip(1))
            AuditActionCatalog.Default.Get(action).TargetType.Should().Be(AuditTargetTypes.Integration, action);
    }

    [Fact]
    public void The_module_change_actions_keep_the_legacy_blocked_outcome_mapping_and_discovery_stays_legacy()
    {
        foreach (var action in ModuleChangeActions)
        {
            var definition = AuditActionCatalog.Default.Get(action);
            definition.TargetType.Should().Be(AuditTargetTypes.Module, action);
            AuditActionCatalog.MapRecordedOutcome(definition, "blocked").Should().Be(AuditOutcomeCodes.Blocked, action);
        }

        var discover = AuditActionCatalog.Default.Get("module.lifecycle.discover");
        discover.Details.Should().BeNull();
        discover.WriteClass.Should().Be(AuditWriteClass.Telemetry);
    }

    [Fact]
    public void The_shutdown_request_keeps_its_declared_identity_and_gains_a_schema()
    {
        var definition = AuditActionCatalog.Default.Get(AuditActionNames.RuntimeShutdownRequestCreated);

        definition.Action.Should().Be("governance.runtime.shutdown.request.created");
        definition.Category.Should().Be(AuditCategories.Operations);
        definition.Severity.Should().Be(AuditSeverities.Standard);
        definition.TargetType.Should().Be(AuditTargetTypes.RuntimeInstance);
        definition.Basis.Should().Be(AuditBases.Request);
        definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation);
        definition.Details!.Fields.Select(f => f.Name).Should().BeEquivalentTo("runtimeId", "requestId", "controlKind");
    }

    [Fact]
    public void No_slice_7_schema_declares_a_field_that_could_hold_a_team_a_duration_a_recipient_a_message_or_free_text()
    {
        var actions = RoutingActions.Concat(ModuleChangeActions).Concat(ModuleBlockedActions).Append(AuditActionNames.RuntimeShutdownRequestCreated);
        foreach (var action in actions)
        {
            var fields = AuditActionCatalog.Default.Get(action).Details!.Fields;
            fields.Select(f => f.Name).Should().NotContain(
                ["teamId", "primaryTeamId", "escalationTeamId", "escalationAfter", "reminderInterval", "recipient", "recipients", "email", "address", "message", "messages", "error", "requestedBy"],
                action);
            fields.Should().NotContain(f => f.Kind == AuditFieldKind.OperatorText, action);
        }
    }

    // ---- Runtime shutdown request ----

    [Fact]
    public void A_shutdown_request_row_targets_the_runtime_scope_and_records_the_request_not_the_requester()
    {
        var evt = AuditEvents.RuntimeShutdownRequestCreated("environment:env1:runtime:rt1", "rt1", 42);

        evt.Action.Should().Be("governance.runtime.shutdown.request.created");
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.RuntimeInstance, "environment:env1:runtime:rt1"));
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Requested);
        evt.Fields.Keys.Should().BeEquivalentTo("runtimeId", "requestId", "controlKind");
        evt.Fields["runtimeId"].Should().Be("rt1");
        evt.Fields["requestId"].Should().Be("42");
        evt.Fields["controlKind"].Should().Be("shutdown");
    }

    [Fact]
    public void A_large_request_id_is_formatted_invariantly()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            AuditEvents.RuntimeShutdownRequestCreated("environment:env1:runtime:rt1", "rt1", 1234567890123).Fields["requestId"]
                .Should().Be("1234567890123");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    // ---- Attention routing ----

    [Fact]
    public void The_closed_change_field_list_is_the_routing_settings_and_nothing_else()
    {
        AuditDetailSchemas.AttentionRoutingChangeFields.Should().Equal(
            "enabled", "sendResolvedNotification", "suppressRemindersWhenAcknowledged", "suppressEscalationWhenAcknowledged",
            "escalationAfter", "reminderInterval", "primaryTeam", "escalationTeam");
    }

    [Fact]
    public void The_instance_default_row_targets_the_instance_and_names_the_fields_that_changed()
    {
        var evt = AuditEvents.AttentionRoutingDefaultUpdated(AttentionConditionKeys.ExpectedRunMissing, ["reminderInterval", "enabled"]);

        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.Instance, AuditEvents.InstanceTargetId));
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        evt.Fields.Keys.Should().BeEquivalentTo("conditionKey", "changedFields");
        evt.Fields["conditionKey"].Should().Be("expected_run_missing");
        evt.Fields["changedFields"].Should().BeEquivalentTo(new[] { "enabled", "reminderInterval" }, "names come back once each, in the declared order");
    }

    [Fact]
    public void An_override_row_targets_the_integration_with_its_name_and_context()
    {
        var created = AuditEvents.AttentionRoutingOverrideCreated("int1", "Sample API Polling", AttentionConditionKeys.ExpectedRunMissing, "ctx1");
        created.Target.Should().Be(new AuditTarget(AuditTargetTypes.Integration, "int1"));
        created.Related.ResourceContextId.Should().Be("ctx1");
        created.Fields.Keys.Should().BeEquivalentTo("name", "conditionKey");
        created.Fields["name"].Should().Be("Sample API Polling");

        var updated = AuditEvents.AttentionRoutingOverrideUpdated("int1", "Sample API Polling", AttentionConditionKeys.ExpectedRunMissing, ["primaryTeam", "escalationTeam"], "ctx1");
        updated.Fields.Keys.Should().BeEquivalentTo("name", "conditionKey", "changedFields");
        updated.Fields["changedFields"].Should().BeEquivalentTo(new[] { "primaryTeam", "escalationTeam" });
        updated.Related.ResourceContextId.Should().Be("ctx1");

        var cleared = AuditEvents.AttentionRoutingOverrideCleared("int1", "Sample API Polling", AttentionConditionKeys.ExpectedRunMissing, null);
        cleared.Fields.Keys.Should().BeEquivalentTo("name", "conditionKey");
        cleared.Related.ResourceContextId.Should().BeNull();
    }

    [Theory]
    [InlineData("some_other_key")]
    [InlineData("")]
    [InlineData("EXPECTED_RUN_MISSING")]
    [InlineData(TextSentinel)]
    public void Any_condition_key_other_than_the_defined_one_is_recorded_as_other(string key)
    {
        var events = new[]
        {
            AuditEvents.AttentionRoutingDefaultUpdated(key, ["enabled"]),
            AuditEvents.AttentionRoutingOverrideCreated("int1", "Demo", key, "ctx1"),
            AuditEvents.AttentionRoutingOverrideUpdated("int1", "Demo", key, ["enabled"], "ctx1"),
            AuditEvents.AttentionRoutingOverrideCleared("int1", "Demo", key, "ctx1"),
        };

        foreach (var evt in events)
        {
            evt.Fields["conditionKey"].Should().Be("other", evt.Action);
            JsonSerializer.Serialize(evt.Fields).Should().NotContain(TextSentinel, evt.Action);
        }
    }

    [Fact]
    public void An_update_that_changes_nothing_or_names_an_unknown_field_is_rejected()
    {
        var key = AttentionConditionKeys.ExpectedRunMissing;

        ((Action)(() => AuditEvents.AttentionRoutingDefaultUpdated(key, []))).Should().Throw<ArgumentException>();
        ((Action)(() => AuditEvents.AttentionRoutingOverrideUpdated("int1", "Demo", key, [], "ctx1"))).Should().Throw<ArgumentException>();
        ((Action)(() => AuditEvents.AttentionRoutingDefaultUpdated(key, ["enabled", "teamId"]))).Should().Throw<ArgumentException>();
        ((Action)(() => AuditEvents.AttentionRoutingOverrideUpdated("int1", "Demo", key, [TextSentinel], "ctx1"))).Should().Throw<ArgumentException>();
        ((Action)(() => AuditEvents.AttentionRoutingOverrideUpdated("int1", "Demo", key, ["Enabled"], "ctx1"))).Should().Throw<ArgumentException>("names are exact");
    }

    [Fact]
    public void Every_closed_change_field_is_accepted_and_a_sentinel_never_reaches_the_details()
    {
        var evt = AuditEvents.AttentionRoutingOverrideUpdated(
            "int1", "Demo", AttentionConditionKeys.ExpectedRunMissing, AuditDetailSchemas.AttentionRoutingChangeFields, "ctx1");

        evt.Fields["changedFields"].Should().BeEquivalentTo(AuditDetailSchemas.AttentionRoutingChangeFields);
        JsonSerializer.Serialize(evt.Fields).Should().NotContain("team-id-1").And.NotContain("00:15:00");
    }

    [Fact]
    public void The_override_upsert_alternates_are_created_and_updated()
    {
        AuditActionNames.AttentionRoutingOverrideUpsertActions.Should().Equal(
            AuditActionNames.AttentionRoutingOverrideCreated, AuditActionNames.AttentionRoutingOverrideUpdated);
    }

    [Fact]
    public void An_overlong_name_snapshot_is_truncated_not_refused()
    {
        var evt = AuditEvents.AttentionRoutingOverrideCreated("int1", new string('n', 256), AttentionConditionKeys.ExpectedRunMissing, "ctx1");

        ((string)evt.Fields["name"]!).Length.Should().BeLessThanOrEqualTo(256);
    }

    // ---- Module lifecycle ----

    [Fact]
    public void An_install_row_names_the_module_its_version_and_the_states_it_moved_between()
    {
        var evt = AuditEvents.ModuleInstalled("sample-module", "1.2.0", ModuleLifecycleState.Discovered, ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate);

        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.Module, "sample-module"));
        evt.Related.ModuleId.Should().Be("sample-module");
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        evt.Fields.Keys.Should().BeEquivalentTo("version", "previousState", "state", "restartEffect");
        evt.Fields["version"].Should().Be("1.2.0");
        evt.Fields["previousState"].Should().Be("discovered");
        evt.Fields["state"].Should().Be("installed_disabled");
        evt.Fields["restartEffect"].Should().Be("immediate");
    }

    [Fact]
    public void An_implicit_installation_is_flagged_only_when_it_happened()
    {
        var implicitInstall = AuditEvents.ModuleInstalled(
            "sample-module", "1.2.0", ModuleLifecycleState.Discovered, ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate, implicitInstall: true);
        implicitInstall.Fields["implicitInstall"].Should().Be(true);

        AuditEvents.ModuleInstalled("sample-module", "1.2.0", ModuleLifecycleState.Discovered, ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate)
            .Fields.Should().NotContainKey("implicitInstall");

        var implicitEnable = AuditEvents.ModuleEnabled(
            "sample-module", "1.2.0", null, ModuleLifecycleState.Discovered, ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart, implicitInstall: true);
        implicitEnable.Fields["implicitInstall"].Should().Be(true);

        AuditEvents.ModuleEnabled("sample-module", "1.2.0", null, ModuleLifecycleState.InstalledDisabled, ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart)
            .Fields.Should().NotContainKey("implicitInstall");
    }

    [Fact]
    public void Enable_disable_and_uninstall_rows_report_the_transition_and_its_restart_effect()
    {
        var enabled = AuditEvents.ModuleEnabled(
            "sample-module", "1.3.0", "1.2.0", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart);
        enabled.Action.Should().Be("module.lifecycle.enable");
        enabled.Fields["previousVersion"].Should().Be("1.2.0");
        enabled.Fields["restartEffect"].Should().Be("partial_until_restart");
        enabled.Fields["state"].Should().Be("installed_enabled");

        AuditEvents.ModuleEnabled("sample-module", "1.2.0", null, ModuleLifecycleState.InstalledDisabled, ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart)
            .Fields.Should().NotContainKey("previousVersion");

        var disabled = AuditEvents.ModuleDisabled(
            "sample-module", "1.2.0", ModuleLifecycleState.InstalledEnabled, ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.RequiresRestart);
        disabled.Action.Should().Be("module.lifecycle.disable");
        disabled.Fields["restartEffect"].Should().Be("requires_restart");
        disabled.Fields["previousState"].Should().Be("installed_enabled");

        var uninstalled = AuditEvents.ModuleUninstalled(
            "sample-module", "1.2.0", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleState.Discovered, ModuleLifecycleEffect.Immediate);
        uninstalled.Action.Should().Be("module.lifecycle.uninstall");
        uninstalled.Fields["state"].Should().Be("discovered");
        uninstalled.Target.Type.Should().Be(AuditTargetTypes.Module);
    }

    [Fact]
    public void A_state_or_effect_outside_the_enums_is_refused()
    {
        ((Action)(() => AuditEvents.ModuleInstalled("m", "1", (ModuleLifecycleState)9, ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate)))
            .Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => AuditEvents.ModuleInstalled("m", "1", ModuleLifecycleState.Discovered, ModuleLifecycleState.InstalledDisabled, (ModuleLifecycleEffect)9)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_blocked_attempt_names_sorted_distinct_blocker_codes_and_never_their_messages()
    {
        var blockers = new[]
        {
            new ModuleLifecycleBlocker("setting_conflict", MessageSentinel),
            new ModuleLifecycleBlocker("dependency_missing", MessageSentinel),
            new ModuleLifecycleBlocker("setting_conflict", MessageSentinel),
        };

        var evt = AuditEvents.ModuleEnableBlocked("sample-module", ModuleLifecycleState.InstalledDisabled, blockers.Select(b => b.Code).ToList());

        evt.Action.Should().Be("module.lifecycle.enable_blocked");
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Blocked);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.Module, "sample-module"));
        evt.Related.ModuleId.Should().Be("sample-module");
        evt.Fields.Keys.Should().BeEquivalentTo("state", "blockers");
        evt.Fields["state"].Should().Be("installed_disabled");
        ((IEnumerable<string>)evt.Fields["blockers"]!).Should().Equal("dependency_missing", "setting_conflict");
        JsonSerializer.Serialize(evt.Fields).Should().NotContain(MessageSentinel).And.NotContain("Password");
    }

    [Fact]
    public void Each_blocked_producer_writes_its_own_action()
    {
        var codes = new[] { "not_installed" };

        AuditEvents.ModuleInstallBlocked("m", ModuleLifecycleState.Discovered, codes).Action.Should().Be("module.lifecycle.install_blocked");
        AuditEvents.ModuleEnableBlocked("m", ModuleLifecycleState.InstalledDisabled, codes).Action.Should().Be("module.lifecycle.enable_blocked");
        AuditEvents.ModuleDisableBlocked("m", ModuleLifecycleState.InstalledEnabled, codes).Action.Should().Be("module.lifecycle.disable_blocked");
        AuditEvents.ModuleUninstallBlocked("m", ModuleLifecycleState.Discovered, codes).Action.Should().Be("module.lifecycle.uninstall_blocked");
    }

    [Fact]
    public void A_blocked_attempt_with_no_usable_code_is_refused_and_the_list_is_bounded()
    {
        ((Action)(() => AuditEvents.ModuleEnableBlocked("m", ModuleLifecycleState.InstalledDisabled, [])))
            .Should().Throw<ArgumentException>();
        ((Action)(() => AuditEvents.ModuleEnableBlocked("m", ModuleLifecycleState.InstalledDisabled, ["", "  ", new string('x', 500)])))
            .Should().Throw<ArgumentException>("a code that cannot be an identifier is not recorded");

        var many = Enumerable.Range(0, 40).Select(i => $"blocker_{i:D2}").ToList();
        var evt = AuditEvents.ModuleEnableBlocked("m", ModuleLifecycleState.InstalledDisabled, many);
        ((IEnumerable<string>)evt.Fields["blockers"]!).Should().HaveCount(16);
    }

    [Fact]
    public void Module_rows_serialize_without_any_sentinel_text()
    {
        var evts = new[]
        {
            AuditEvents.ModuleEnableBlocked("sample-module", ModuleLifecycleState.InstalledDisabled, ["setting_conflict"]),
            AuditEvents.ModuleEnabled("sample-module", "1.0.0", null, ModuleLifecycleState.InstalledDisabled, ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.Immediate),
        };

        foreach (var evt in evts)
            JsonSerializer.Serialize(evt.Fields).Should().NotContain(MessageSentinel).And.NotContain(TextSentinel);
    }
}
