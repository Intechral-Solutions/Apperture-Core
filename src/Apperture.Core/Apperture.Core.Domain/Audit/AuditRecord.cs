using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Audit;

/// <summary>
/// Immutable audit entry. Entity (append-only).
/// </summary>
public sealed record AuditRecord(
    AuditRecordId Id,
    ActorId ActorId,
    string Action,
    ResourceDescriptor? Resource,
    string Outcome,
    DateTimeOffset Timestamp,
    CorrelationId CorrelationId,
    string? Details);
