namespace Apperture.Core.Application.PlatformEvents;

/// <summary>Request to append a platform event. Id and RecordedAtUtc are assigned by the emitter when omitted.</summary>
public sealed record PlatformEventEmitRequest(
    string Type,
    string Source,
    string DataJson,
    DateTimeOffset OccurredAtUtc,
    string Severity,
    string? Subject = null,
    string? DedupeKey = null,
    string? IntegrationId = null,
    string? ModuleId = null,
    string? ProcedureId = null,
    string? RunId = null,
    string? ScheduleId = null,
    string? ActorId = null,
    string? CorrelationId = null,
    string? CausationId = null,
    string DataContentType = "application/json",
    string SpecVersion = "1.0",
    PlatformEventId? Id = null);
