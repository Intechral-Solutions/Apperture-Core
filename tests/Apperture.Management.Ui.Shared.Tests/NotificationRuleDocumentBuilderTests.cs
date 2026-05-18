using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class NotificationRuleDocumentBuilderTests
{
    [Fact]
    public void Build_pins_integration_and_event_type()
    {
        var json = NotificationRuleDocumentBuilder.Build(
            "int-1",
            "apperture.schedule.expected_run.missing",
            severityOperator: "in",
            severityValue: "warning,error",
            moduleId: "mod.sample",
            propertyFilters:
            [
                new NotificationPropertyFilterRow { DataField = "evaluationStatus", Operator = "equals", Value = "missing" },
            ]);

        NotificationRuleDocumentBuilder.TryParse(json, "int-1", out var state).Should().BeTrue();
        state.EventType.Should().Be("apperture.schedule.expected_run.missing");
        state.SeverityOperator.Should().Be("in");
        state.ModuleId.Should().Be("mod.sample");
        state.PropertyFilters.Should().ContainSingle(f => f.DataField == "evaluationStatus");
        json.Should().Contain("\"integrationId\"");
        json.Should().Contain("int-1");
    }

    [Fact]
    public void TryParse_returns_false_for_expression_stage()
    {
        const string json = """
            {
              "schemaVersion": "apperture.notification.rule/v1",
              "trigger": { "eventTypes": ["apperture.schedule.expected_run.missing"] },
              "stages": [
                { "stage": "index", "filters": { "integrationId": { "operator": "equals", "value": "int-1" }, "type": { "operator": "in", "values": ["apperture.schedule.expected_run.missing"] } } },
                { "stage": "expression", "expression": "true" }
              ]
            }
            """;

        NotificationRuleDocumentBuilder.TryParse(json, "int-1", out _).Should().BeFalse();
    }
}
