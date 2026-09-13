namespace Apperture.ModuleSdk.Testing.Signals;

/// <summary>Captured module platform event from <see cref="RecordingModulePlatformEventEmitter"/>.</summary>
public sealed record RecordedPlatformEvent(
    string SignalName,
    string EventType,
    string Source,
    string Severity,
    string DataJson,
    string Subject,
    string ModuleId,
    string? IntegrationId,
    string? RunId,
    string? ProcedureId,
    string? ScheduleId,
    string? CorrelationId,
    string? CausationId,
    DateTimeOffset OccurredAtUtc,
    string EventId);
