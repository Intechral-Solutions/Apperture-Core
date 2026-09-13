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
    string? RunId = null,
    /// <summary>
    /// CTX-2: the authoritative ResourceContext the caller already knows for this occurrence (e.g. an
    /// already-loaded Integration/Run/ScheduleDefinition/OperationGroup). Never inferred here from
    /// other ids. Null for System/IAM/platform-settings audit and for callers with no authoritative
    /// context available.
    /// </summary>
    string? ResourceContextId = null);
