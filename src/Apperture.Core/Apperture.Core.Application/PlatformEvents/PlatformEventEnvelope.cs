namespace Apperture.Core.Application.PlatformEvents;

/// <summary>
/// Durable platform event (CloudEvents-inspired, Apperture-native). Epic 77.
/// </summary>
public sealed record PlatformEventEnvelope(
    PlatformEventId Id,
    string Type,
    string Source,
    string SpecVersion,
    string DataContentType,
    string DataJson,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset RecordedAtUtc,
    string Severity,
    string? Subject,
    string? DedupeKey,
    string? IntegrationId,
    string? ModuleId,
    string? ProcedureId,
    string? RunId,
    string? ScheduleId,
    string? ActorId,
    string? CorrelationId,
    string? CausationId,
    /// <summary>CTX-2: immutable ResourceContext snapshot inherited from the causal run/schedule/incident.</summary>
    string? ResourceContextId = null);
