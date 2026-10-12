using System.Text.Json;
using Apperture.Core.Application.Audit;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.4 slice 6: the catalog entries, detail schemas and typed producers of the notification definition, subscription, managed
/// contact and diagnostic families (design gate §11, §12, §13.1; M7). The transactional behaviour is proven by the service tests
/// and the SQL Server suites.
/// </summary>
public sealed class Slice6AuditEventTests
{
    private const string AddressSentinel = "someone.private@example.com";

    private static readonly string[] DefinitionActions =
    [
        AuditActionNames.NotificationDefinitionCreated, AuditActionNames.NotificationDefinitionUpdated,
        AuditActionNames.NotificationDefinitionEnabled, AuditActionNames.NotificationDefinitionDisabled,
        AuditActionNames.NotificationDefinitionDeleted,
    ];

    private static readonly string[] SubscriptionActions =
    [
        AuditActionNames.NotificationSubscriptionAdded, AuditActionNames.NotificationSubscriptionRemoved,
        AuditActionNames.NotificationSubscriptionUpdated, AuditActionNames.NotificationSubscriptionUserOptOutSet,
        AuditActionNames.NotificationSubscriptionUserOptOutRemoved,
    ];

    private static readonly string[] ContactActions =
    [
        AuditActionNames.ManagedContactCreated, AuditActionNames.ManagedContactUpdated, AuditActionNames.ManagedContactUserLinked,
        AuditActionNames.ManagedContactUserUnlinked, AuditActionNames.ManagedContactGroupImported,
    ];

    private static readonly string[] DiagnosticActions =
    [
        AuditActionNames.NotificationSmtpTestAttempted, AuditActionNames.NotificationPipelineTestAttempted,
        AuditActionNames.NotificationDiagnosticsTestRefused,
    ];

    [Fact]
    public void Every_action_has_a_structured_schema()
    {
        foreach (var action in DefinitionActions.Concat(SubscriptionActions).Concat(ContactActions).Concat(DiagnosticActions))
            AuditActionCatalog.Default.Get(action).Details.Should().NotBeNull(action);
    }

    [Fact]
    public void The_definition_subscription_and_contact_writers_are_required_mutations_that_commit_with_their_change()
    {
        foreach (var action in DefinitionActions.Concat(SubscriptionActions).Concat(ContactActions))
        {
            var definition = AuditActionCatalog.Default.Get(action);
            definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation, action);
            definition.Basis.Should().Be(AuditBases.Change, action);
            definition.Category.Should().Be(AuditCategories.Configuration, action);
        }
    }

    [Fact]
    public void A_live_diagnostic_send_is_an_external_attempt_and_a_refusal_before_any_call_is_an_attempt()
    {
        foreach (var action in new[] { AuditActionNames.NotificationSmtpTestAttempted, AuditActionNames.NotificationPipelineTestAttempted })
        {
            var definition = AuditActionCatalog.Default.Get(action);
            definition.WriteClass.Should().Be(AuditWriteClass.Attempt, action);
            definition.Basis.Should().Be(AuditBases.ExternalAttempt, action);
        }

        var refused = AuditActionCatalog.Default.Get(AuditActionNames.NotificationDiagnosticsTestRefused);
        refused.WriteClass.Should().Be(AuditWriteClass.Attempt);
        refused.Basis.Should().Be(AuditBases.Attempt);
    }

    [Fact]
    public void Each_action_targets_its_own_entity()
    {
        foreach (var action in DefinitionActions)
            AuditActionCatalog.Default.Get(action).TargetType.Should().Be(AuditTargetTypes.NotificationDefinition, action);
        foreach (var action in SubscriptionActions)
            AuditActionCatalog.Default.Get(action).TargetType.Should().Be(AuditTargetTypes.NotificationSubscription, action);
        foreach (var action in ContactActions)
            AuditActionCatalog.Default.Get(action).TargetType.Should().Be(AuditTargetTypes.ManagedContact, action);
        AuditActionCatalog.Default.Get(AuditActionNames.NotificationPipelineTestAttempted).TargetType.Should().Be(AuditTargetTypes.NotificationDefinition);
        AuditActionCatalog.Default.Get(AuditActionNames.NotificationSmtpTestAttempted).TargetType.Should().Be(AuditTargetTypes.Instance);
    }

    [Fact]
    public void No_contact_or_diagnostic_event_declares_a_field_that_could_hold_an_address_a_message_or_a_name()
    {
        foreach (var action in ContactActions.Concat(DiagnosticActions))
        {
            var fields = AuditActionCatalog.Default.Get(action).Details!.Fields.Select(f => f.Name);
            fields.Should().NotContain(["email", "emailAddress", "displayName", "name", "recipient", "recipients", "subject", "body", "error", "message"], action);
            AuditActionCatalog.Default.Get(action).Details!.Fields.Should().NotContain(f => f.Kind == AuditFieldKind.OperatorText || f.Kind == AuditFieldKind.NameSnapshot, action);
        }
    }

    [Fact]
    public void A_definition_event_names_the_definition_and_never_its_rule_or_template()
    {
        var created = AuditEvents.NotificationDefinitionCreated("nd1", "Missed run", "int1", enabled: true, toastEnabledByDefault: false, "warning", "platform.schedule.expected_run_missing", "ctx1");
        created.Target.Should().Be(new AuditTarget(AuditTargetTypes.NotificationDefinition, "nd1"));
        created.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        created.Related.ResourceContextId.Should().Be("ctx1");
        created.Fields.Keys.Should().BeEquivalentTo("name", "integrationId", "enabled", "toastEnabledByDefault", "defaultSeverity", "primaryEventType");

        var updated = AuditEvents.NotificationDefinitionUpdated("nd1", "Missed run", "int1", ["rule", "template"], "ctx1");
        updated.Fields["changedFields"].Should().BeEquivalentTo(new[] { "rule", "template" });
        updated.Fields.Keys.Should().BeEquivalentTo("name", "integrationId", "changedFields");

        var unchanged = () => AuditEvents.NotificationDefinitionUpdated("nd1", "Missed run", "int1", [], "ctx1");
        unchanged.Should().Throw<ArgumentException>("an update that changes nothing is not audited");

        AuditEvents.NotificationDefinitionEnableChanged("nd1", "Missed run", "int1", enabled: true, "ctx1").Action.Should().Be(AuditActionNames.NotificationDefinitionEnabled);
        AuditEvents.NotificationDefinitionEnableChanged("nd1", "Missed run", "int1", enabled: false, "ctx1").Action.Should().Be(AuditActionNames.NotificationDefinitionDisabled);

        var deleted = AuditEvents.NotificationDefinitionDeleted("nd1", "Missed run", "int1", 3, 2, 5, "ctx1");
        deleted.Fields["subscriptionsRemoved"].Should().Be(3);
        deleted.Fields["userOptOutsRemoved"].Should().Be(2);
        deleted.Fields["userPreferencesRemoved"].Should().Be(5);
    }

    [Fact]
    public void A_subscription_event_names_the_recipient_by_id_and_a_removed_one_keeps_its_id()
    {
        var added = AuditEvents.NotificationSubscriptionAdded("sub1", "nd1", "int1", "managedContact", "mc-1", "immediate", managedContactCreated: true, "ctx1");
        added.Target.Should().Be(new AuditTarget(AuditTargetTypes.NotificationSubscription, "sub1"));
        added.Fields["recipientKind"].Should().Be("managedContact");
        added.Fields["recipientRef"].Should().Be("mc-1");
        added.Fields["managedContactCreated"].Should().Be(true);

        var plain = AuditEvents.NotificationSubscriptionAdded("sub2", "nd1", null, "principal", "u1", "digestOnly", managedContactCreated: false, null);
        plain.Fields.Should().NotContainKey("managedContactCreated");
        plain.Fields.Should().NotContainKey("integrationId");

        AuditEvents.NotificationSubscriptionRemoved("sub1", "nd1", "int1", "managedContact", "mc-1", "ctx1").Fields["recipientRef"].Should().Be("mc-1");
        AuditEvents.NotificationSubscriptionUpdated("sub1", "nd1", "int1", "delivery_mode", "muted", "ctx1").Fields["deliveryMode"].Should().Be("muted");

        var optOut = AuditEvents.NotificationUserOptOutSet("oo1", "nd1", "u1", "team1", "ctx1");
        optOut.Fields.Keys.Should().BeEquivalentTo("definitionId", "userId", "operationalTeamId");
        AuditEvents.NotificationUserOptOutRemoved("oo1", "nd1", "u1", null, null).Action.Should().Be(AuditActionNames.NotificationSubscriptionUserOptOutRemoved);
    }

    [Fact]
    public void A_contact_event_carries_ids_kinds_and_flags_and_nothing_of_the_person()
    {
        var created = AuditEvents.ManagedContactCreated("mc-1", "custom", "subscription", enabled: true, hasEmail: true);
        created.Fields.Keys.Should().BeEquivalentTo("source", "via", "enabled", "hasEmail");
        created.Target.Should().Be(new AuditTarget(AuditTargetTypes.ManagedContact, "mc-1"));

        var updated = AuditEvents.ManagedContactUpdated("mc-1", "entra", ["email", "entraSync"], enabled: true);
        updated.Fields["changedFields"].Should().BeEquivalentTo(new[] { "email", "entraSync" });
        var unchanged = () => AuditEvents.ManagedContactUpdated("mc-1", "entra", [], enabled: true);
        unchanged.Should().Throw<ArgumentException>();

        AuditEvents.ManagedContactUserLinked("mc-1", "u2", "u1").Fields["previousUserId"].Should().Be("u1");
        AuditEvents.ManagedContactUserLinked("mc-1", "u2", null).Fields.Should().NotContainKey("previousUserId");
        AuditEvents.ManagedContactUserUnlinked("mc-1", "u2").Fields["previousUserId"].Should().Be("u2");
        AuditEvents.ManagedContactGroupImported("mctgrp_1", created: false).Fields["created"].Should().Be(false);

        foreach (var evt in new[] { created, updated })
            JsonSerializer.Serialize(evt.Fields).Should().NotContain(AddressSentinel);
        ContactActions.Should().OnlyContain(a => AuditActionCatalog.Default.Get(a).Details!.Fields.All(f => f.Kind != AuditFieldKind.OperatorText));
    }

    [Theory]
    [InlineData("accepted", AuditOutcomeCodes.Succeeded)]
    [InlineData("rejected", AuditOutcomeCodes.Failed)]
    [InlineData("failed", AuditOutcomeCodes.Failed)]
    [InlineData("no_answer", AuditOutcomeCodes.Unknown)]
    public void An_smtp_test_records_what_the_transport_reported_and_acceptance_is_not_delivery(string result, string outcome)
    {
        var evt = AuditEvents.NotificationSmtpTestAttempted(result);

        evt.OutcomeCode.Should().Be(outcome);
        evt.Fields.Keys.Should().BeEquivalentTo("result");
        evt.Fields["result"].Should().Be(result);
        evt.Target.Type.Should().Be(AuditTargetTypes.Instance);
    }

    [Fact]
    public void An_smtp_test_refuses_a_result_the_transport_cannot_report()
    {
        var act = () => AuditEvents.NotificationSmtpTestAttempted("delivered");

        act.Should().Throw<ArgumentException>("delivery to a mailbox is never observed");
    }

    [Theory]
    [InlineData(false, 0, 0, 0, 0, 0, AuditOutcomeCodes.Requested)]
    [InlineData(true, 3, 0, 0, 0, 0, AuditOutcomeCodes.Requested)]
    [InlineData(true, 3, 3, 0, 0, 0, AuditOutcomeCodes.Succeeded)]
    [InlineData(true, 3, 2, 1, 0, 0, AuditOutcomeCodes.Failed)]
    [InlineData(true, 3, 2, 0, 1, 0, AuditOutcomeCodes.Failed)]
    [InlineData(true, 3, 1, 1, 0, 1, AuditOutcomeCodes.Unknown)]
    public void A_pipeline_test_outcome_follows_the_sends_it_made_not_the_request(
        bool dispatch, int outboxRows, int accepted, int rejected, int failed, int unknown, string outcome)
    {
        var attempted = accepted + rejected + failed + unknown;

        var evt = AuditEvents.NotificationPipelineTestAttempted("nd1", "int1", dispatch, 1, outboxRows, attempted, accepted, rejected, failed, unknown, "ctx1");

        evt.OutcomeCode.Should().Be(outcome);
        evt.Fields["sendsAttempted"].Should().Be(attempted);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.NotificationDefinition, "nd1"));
    }

    [Fact]
    public void A_refused_test_is_a_rejected_attempt_with_a_closed_reason()
    {
        var evt = AuditEvents.NotificationDiagnosticsTestRefused("smtp", "not_configured");

        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Rejected);
        evt.Fields.Keys.Should().BeEquivalentTo("test", "reason");
        var act = () => AuditEvents.NotificationDiagnosticsTestRefused("smtp", "The password was wrong for someone.private@example.com");
        act.Should().Throw<Exception>("a reason is one of a closed set, never free text");
    }
}
