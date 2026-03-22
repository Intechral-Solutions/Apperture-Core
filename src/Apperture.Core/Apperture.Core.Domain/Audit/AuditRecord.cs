using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Audit;

/// <summary>
/// Immutable audit entry. Entity (append-only).
/// Epic 37: optional first-class reference fields for DB-backed filtering.
/// </summary>
public sealed record AuditRecord(
    AuditRecordId Id,
    ActorId ActorId,
    string Action,
    ResourceDescriptor? Resource,
    string Outcome,
    DateTimeOffset Timestamp,
    CorrelationId CorrelationId,
    string? Details,
    string? ModuleId = null,
    string? ScheduleId = null,
    string? RunId = null);
