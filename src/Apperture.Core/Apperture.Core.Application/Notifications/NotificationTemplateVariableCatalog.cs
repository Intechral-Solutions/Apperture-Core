using System.Text.Json;
using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions.Signals;

namespace Apperture.Core.Application.Notifications;

public sealed class NotificationTemplateVariableCatalog : INotificationTemplateVariableCatalog
{
    private static readonly IReadOnlySet<string> StandardPaths =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "event.type",
            "event.time",
            "event.severity",
            "integration.id",
            "integration.displayName",
            "module.id",
            "module.displayName",
            "run.id",
            "instance.baseUrl",
            "notification.kind",
            "incident.id",
            "incident.startedAt",
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> PlatformAllowlists =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [PlatformEventTypes.ScheduleExpectedRunMissing] = new HashSet<string>(StringComparer.Ordinal)
            {
                "event.type",
                "event.time",
                "event.severity",
                "event.data.evaluationStatus",
                "event.data.operatorSummaryLine",
                "event.data.mostRecentExpectedFireUtc",
                "event.data.missingMinutes",
                "integration.id",
                "integration.displayName",
                "schedule.id",
                "schedule.name",
                "incident.id",
                "incident.startedAt",
                "notification.kind",
                "instance.baseUrl",
            },
            [PlatformEventTypes.ScheduleExpectedRunCleared] = new HashSet<string>(StringComparer.Ordinal)
            {
                "event.type",
                "event.time",
                "event.severity",
                "event.data.evaluationStatus",
                "event.data.resolutionReason",
                "integration.id",
                "integration.displayName",
                "schedule.id",
                "schedule.name",
                "incident.id",
                "incident.startedAt",
                "notification.kind",
                "instance.baseUrl",
            },
            [PlatformEventTypes.ExecutionRunCompleted] = new HashSet<string>(StringComparer.Ordinal)
            {
                "event.type",
                "event.time",
                "event.severity",
                "event.data.executionKind",
                "event.data.definitionId",
                "event.data.status",
                "event.data.outcome",
                "event.data.durationMs",
                "event.data.stepCount",
                "integration.id",
                "integration.displayName",
                "run.id",
                "run.definitionId",
                "module.id",
                "module.displayName",
                "instance.baseUrl",
            },
            [PlatformEventTypes.ExecutionRunFailed] = new HashSet<string>(StringComparer.Ordinal)
            {
                "event.type",
                "event.time",
                "event.severity",
                "event.data.executionKind",
                "event.data.definitionId",
                "event.data.status",
                "event.data.failureCode",
                "event.data.failureMessageExcerpt",
                "event.data.durationMs",
                "event.data.failedStepName",
                "event.data.exceptionType",
                "integration.id",
                "integration.displayName",
                "run.id",
                "run.definitionId",
                "module.id",
                "module.displayName",
                "instance.baseUrl",
            },
        };

    private readonly IModuleSignalRegistry _moduleSignals;

    public NotificationTemplateVariableCatalog(IModuleSignalRegistry moduleSignals) =>
        _moduleSignals = moduleSignals;

    public IReadOnlySet<string> GetAllowlist(string eventType)
    {
        if (PlatformAllowlists.TryGetValue(eventType, out var platform))
            return platform;

        if (_moduleSignals.ModuleDescriptorsByType.TryGetValue(eventType, out var descriptor))
            return BuildModuleAllowlist(descriptor);

        return new HashSet<string>(StringComparer.Ordinal);
    }

    public IReadOnlySet<string> GetUnionAllowlist(IReadOnlyList<string> eventTypes)
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var eventType in eventTypes)
        {
            foreach (var path in GetAllowlist(eventType))
                union.Add(path);
        }

        return union;
    }

    private static IReadOnlySet<string> BuildModuleAllowlist(PlatformEventTypeDescriptor descriptor)
    {
        var set = new HashSet<string>(StandardPaths, StringComparer.Ordinal);
        foreach (var prop in descriptor.RequiredDataProperties)
        {
            var segment = ToCamelCase(prop);
            set.Add($"event.data.{segment}");
        }

        return set;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        if (name.Length == 1)
            return name.ToLowerInvariant();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
