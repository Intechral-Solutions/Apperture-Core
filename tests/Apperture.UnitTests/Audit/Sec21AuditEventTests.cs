using System.Text.RegularExpressions;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// SEC-2.1: the typed producer of <c>attention_incident.acknowledged</c>. The row names the integration (as a snapshot), the
/// incident, its condition, when it started and whether a note was given. It has no field that could hold the note's text, a
/// recipient or notification content, and a caller's text never reaches a field.
/// </summary>
public sealed class Sec21AuditEventTests
{
    private const string TextSentinel = "customer supplied condition text";
    private static readonly DateTimeOffset Started = DateTimeOffset.Parse("2025-01-10T12:00:00Z");

    private static AuditEvent Acknowledged(string conditionKey = AttentionConditionKeys.ExpectedRunMissing, bool noteProvided = true, string? context = "ctx1") =>
        AuditEvents.AttentionIncidentAcknowledged("int1", "Sample API Polling", "attn_0123", conditionKey, Started, noteProvided, context);

    [Fact]
    public void The_row_targets_the_integration_and_carries_the_incident_as_a_field()
    {
        var evt = Acknowledged();

        evt.Action.Should().Be(AuditActionNames.AttentionIncidentAcknowledged);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.Integration, "int1"));
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        evt.Related.ResourceContextId.Should().Be("ctx1");
        evt.Fields.Keys.Should().BeEquivalentTo("name", "incidentId", "conditionKey", "incidentStartedAt", "noteProvided");
        evt.Fields["name"].Should().Be("Sample API Polling");
        evt.Fields["incidentId"].Should().Be("attn_0123");
        evt.Fields["conditionKey"].Should().Be("expected_run_missing");
        evt.Fields["incidentStartedAt"].Should().Be(Started);
        evt.Fields["noteProvided"].Should().Be(true);
    }

    [Fact]
    public void Whether_a_note_was_given_is_recorded_and_the_owning_context_is_optional()
    {
        var evt = Acknowledged(noteProvided: false, context: null);

        evt.Fields["noteProvided"].Should().Be(false);
        evt.Related.ResourceContextId.Should().BeNull();
    }

    [Theory]
    [InlineData("some_other_key")]
    [InlineData("")]
    [InlineData("EXPECTED_RUN_MISSING")]
    [InlineData(TextSentinel)]
    public void Any_condition_key_other_than_the_defined_one_is_recorded_as_other_and_its_text_reaches_no_field(string key)
    {
        var evt = Acknowledged(key);

        evt.Fields["conditionKey"].Should().Be("other");
        evt.Fields.Values.OfType<string>().Should().NotContain(s => s.Contains("customer supplied", StringComparison.Ordinal));
    }

    [Fact]
    public void The_serialized_details_hold_the_declared_fields_in_the_json_1_form()
    {
        var definition = AuditActionCatalog.Default.Get(AuditActionNames.AttentionIncidentAcknowledged);

        var details = definition.Details!.Serialize(definition.Action, definition.WriteClass, Acknowledged().Fields);

        details.Omitted.Should().BeFalse();
        details.Truncated.Should().BeEmpty();
        using var json = System.Text.Json.JsonDocument.Parse(details.Json);
        json.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("name", "incidentId", "conditionKey", "incidentStartedAt", "noteProvided");
        json.RootElement.GetProperty("incidentStartedAt").GetString().Should().Be("2025-01-10T12:00:00.0000000+00:00");
        json.RootElement.GetProperty("noteProvided").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void An_incident_id_the_audit_schema_cannot_hold_is_refused_by_the_producer()
    {
        var act = () => AuditEvents.AttentionIncidentAcknowledged(
            "int1", "Demo", new string('a', AuditDetailSchema.MaxIdLength + 1), AttentionConditionKeys.ExpectedRunMissing, Started, false, null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_schema_declares_no_field_that_could_hold_a_note_a_recipient_or_notification_content()
    {
        var definition = AuditActionCatalog.Default.Get(AuditActionNames.AttentionIncidentAcknowledged);
        var names = definition.Details!.Fields.Select(f => f.Name).ToList();

        names.Should().BeEquivalentTo(["name", "incidentId", "conditionKey", "incidentStartedAt", "noteProvided"]);
        names.Should().NotContain(n => Regex.IsMatch(n, "^(note|noteText|acknowledgementNote|recipients?|recipientRef|email|subject|body|message|content)$", RegexOptions.IgnoreCase),
            "no field is named for the note's text, a recipient or notification content");
        names.Should().NotContain(n => AuditDetailSchema.IsSensitiveFieldName(n));
        definition.Details.Fields.Should().NotContain(f => f.Kind == AuditFieldKind.OperatorText, "no operator-typed text is recorded");
    }

    [Fact]
    public void The_catalog_entry_is_the_approved_one()
    {
        var definition = AuditActionCatalog.Default.Get(AuditActionNames.AttentionIncidentAcknowledged);

        definition.Category.Should().Be(AuditCategories.Operations);
        definition.Severity.Should().Be(AuditSeverities.Standard);
        definition.Basis.Should().Be(AuditBases.Change);
        definition.TargetType.Should().Be(AuditTargetTypes.Integration);
        definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation);
        definition.IntroducedIn.Should().Be("SEC-2.1");
        definition.Retired.Should().BeFalse();
    }
}
