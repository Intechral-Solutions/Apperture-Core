using System.Text.Json;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Scheduling;
using Apperture.ModuleSdk.Abstractions;
using Sample.HelloWorld;

namespace Apperture.UnitTests.Notifications;

internal static class NotificationEvaluationTestHelpers
{
    internal static NotificationRuleEvaluator CreateEvaluator() =>
        new(
            new NotificationTemplateVariableCatalog(CreateRegistry()),
            new InMemoryNotificationThrottleStore(),
            new NotificationRenderContextBuilder());

    internal static IModuleSignalRegistry CreateRegistry()
    {
        IModule[] modules = [new HelloWorldModule()];
        ModuleSignalManifestValidator.ThrowIfInvalid(modules);
        return ModuleSignalRegistry.FromModules(modules);
    }

    internal static PlatformEventEnvelope SampleMissingEvent(string integrationId, string? incidentId)
    {
        var data = new Dictionary<string, object?>
        {
            [PlatformEventDataProperties.EvaluationStatus] = "MissingExpectedRun",
            [PlatformEventDataProperties.OperatorSummaryLine] = "summary",
            [PlatformEventDataProperties.MostRecentExpectedFireUtc] = "2026-05-15T10:00:00Z",
        };
        if (incidentId is not null)
            data[PlatformEventDataProperties.AttentionIncidentId] = incidentId;

        return new PlatformEventEnvelope(
            PlatformEventId.New(),
            PlatformEventTypes.ScheduleExpectedRunMissing,
            PlatformEventSources.PlatformReadiness,
            "1.0",
            "application/json",
            JsonSerializer.Serialize(data),
            DateTimeOffset.Parse("2026-05-15T12:00:00Z"),
            DateTimeOffset.Parse("2026-05-15T12:00:01Z"),
            PlatformEventSeverities.Warning,
            $"{integrationId}:expected_run_missing",
            $"{integrationId}:expected_run_missing",
            integrationId,
            null,
            null,
            null,
            "sched-1",
            null,
            null,
            null);
    }

    internal static PlatformEventEnvelope SampleClearedEvent(string integrationId, string? incidentId)
    {
        var data = new Dictionary<string, object?>
        {
            [PlatformEventDataProperties.EvaluationStatus] = "Satisfied",
            [PlatformEventDataProperties.ResolutionReason] = "ExpectationCleared",
            [PlatformEventDataProperties.AttentionIncidentId] = incidentId,
        };

        return new PlatformEventEnvelope(
            PlatformEventId.New(),
            PlatformEventTypes.ScheduleExpectedRunCleared,
            PlatformEventSources.PlatformReadiness,
            "1.0",
            "application/json",
            JsonSerializer.Serialize(data),
            DateTimeOffset.Parse("2026-05-15T13:00:00Z"),
            DateTimeOffset.Parse("2026-05-15T13:00:01Z"),
            PlatformEventSeverities.Info,
            $"{integrationId}:expected_run_missing:cleared",
            null,
            integrationId,
            null,
            null,
            null,
            "sched-1",
            null,
            null,
            null);
    }

    internal static ScheduleExpectationSnapshot SampleSnapshot() =>
        new(
            new ScheduleExpectationEvaluation(
                ExpectsRecurringRuns: true,
                HasExpectedFireInWindow: true,
                ExpectedFireCountInWindow: 1,
                MostRecentExpectedFireUtc: DateTimeOffset.Parse("2026-05-15T10:00:00Z"),
                InGracePeriod: false,
                SatisfiedByAuthorizedScheduleOriginRun: false,
                LastSatisfyingScheduleOriginRunId: null,
                LastSatisfyingScheduleOriginRunAt: null,
                Status: ScheduleExpectationEvaluationStatus.MissingExpectedRun,
                OperatorSummaryLine: "Missing expected run"),
            "sched-1",
            null,
            DateTimeOffset.Parse("2026-05-15T09:00:00Z"));
}
