using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Domain.Notifications;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

public sealed class NotificationRuleEvaluatorTests
{
    private readonly NotificationRuleEvaluator _sut = NotificationEvaluationTestHelpers.CreateEvaluator();

    [Fact]
    public async Task Missing_platform_event_matches_builtin_definition_as_activation()
    {
        var def = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1");
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            null,
            envelope.RecordedAtUtc);

        var result = await _sut.EvaluateDefinitionAsync(def, context);
        result.Status.Should().Be(NotificationMatchStatus.Matched);
        result.NotificationKind.Should().Be(NotificationKinds.Activation);
        result.TemplateVariantKey.Should().Be(NotificationKinds.Activation);
    }

    [Fact]
    public async Task Cleared_platform_event_matches_builtin_as_resolved()
    {
        var def = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var envelope = NotificationEvaluationTestHelpers.SampleClearedEvent("int-1", "inc-1");
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            null,
            envelope.RecordedAtUtc);

        var result = await _sut.EvaluateDefinitionAsync(def, context);
        result.Status.Should().Be(NotificationMatchStatus.Matched);
        result.NotificationKind.Should().Be(NotificationKinds.Resolved);
    }

    [Fact]
    public async Task Platform_event_append_does_not_produce_reminder()
    {
        var def = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1");
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            null,
            envelope.RecordedAtUtc);

        var result = await _sut.EvaluateDefinitionAsync(def, context);
        result.NotificationKind.Should().NotBe(NotificationKinds.Reminder);
    }

    [Fact]
    public async Task ObservedStillActive_incident_context_produces_reminder()
    {
        var def = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var incident = new IncidentEvaluationContext(
            "int-1",
            AttentionIncidentObserveTransition.ObservedStillActive,
            "inc-1",
            NotificationEvaluationTestHelpers.SampleSnapshot(),
            DateTimeOffset.Parse("2026-05-15T12:00:00Z"));
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.IncidentTransition,
            null,
            incident,
            incident.ObservedAtUtc);

        var result = await _sut.EvaluateDefinitionAsync(def, context);
        result.Status.Should().Be(NotificationMatchStatus.Matched);
        result.NotificationKind.Should().Be(NotificationKinds.Reminder);
    }

    [Fact]
    public async Task RequiresIncident_fails_without_incident_id()
    {
        var def = ExpectedRunMissingNotificationDefinitionSeed.BuiltIn(DateTimeOffset.UtcNow);
        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", incidentId: null);
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            null,
            envelope.RecordedAtUtc);

        var result = await _sut.EvaluateDefinitionAsync(def, context);
        result.Status.Should().Be(NotificationMatchStatus.NoMatch);
    }

    [Fact]
    public async Task Throttle_perSubjectMinInterval_suppresses_second_match()
    {
        var throttle = new InMemoryNotificationThrottleStore();
        var sut = new NotificationRuleEvaluator(
            new NotificationTemplateVariableCatalog(NotificationEvaluationTestHelpers.CreateRegistry()),
            throttle,
            new NotificationRenderContextBuilder());

        var rule = """
            {
              "schemaVersion": "apperture.notification.rule/v1",
              "trigger": { "eventTypes": ["apperture.schedule.expected_run.missing"] },
              "stages": [
                {
                  "stage": "index",
                  "filters": {
                    "type": { "operator": "in", "values": ["apperture.schedule.expected_run.missing"] }
                  }
                }
              ],
              "throttle": { "perSubjectMinInterval": "1h" }
            }
            """;
        var def = new NotificationDefinition(
            NotificationDefinitionId.New(),
            "Throttle test",
            null,
            NotificationSourceKinds.PlatformEvent,
            NotificationDefinitionOrigins.UserCreated,
            NotificationDefinitionScopes.Integration,
            NotificationDefinitionMutabilities.FullyEditable,
            null,
            "int-1",
            PlatformEventTypes.ScheduleExpectedRunMissing,
            true,
            null,
            rule,
            NotificationValidationTestHelpers.ValidUserTemplateJson,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null);

        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-1", "inc-1");
        var context = new NotificationRuleEvaluationContext(
            NotificationEvaluationSourceKind.PlatformEvent,
            envelope,
            null,
            envelope.RecordedAtUtc);

        var first = await sut.EvaluateDefinitionAsync(def, context);
        first.Status.Should().Be(NotificationMatchStatus.Matched);

        var second = await sut.EvaluateDefinitionAsync(def, context);
        second.Status.Should().Be(NotificationMatchStatus.Suppressed);
        second.SuppressionReason.Should().Be("throttled");
    }

    [Fact]
    public void Render_context_builder_sets_incident_and_data_fields()
    {
        var builder = new NotificationRenderContextBuilder();
        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-99", "inc-abc");
        var ctx = builder.Build(envelope, NotificationKinds.Activation, NotificationKinds.Activation);

        ctx["integration.id"].Should().Be("int-99");
        ctx["notification.kind"].Should().Be(NotificationKinds.Activation);
        ctx["incident.id"].Should().Be("inc-abc");
        ctx["event.data.evaluationStatus"].Should().NotBeNullOrEmpty();
    }
}
