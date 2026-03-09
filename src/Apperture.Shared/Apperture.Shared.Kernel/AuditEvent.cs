namespace Apperture.Shared.Kernel;

/// <summary>
/// Primitive audit event for append-only sink. No persistence; used for cross-cutting audit contract.
/// </summary>
public sealed record AuditEvent(
    ActorId ActorId,
    string Action,
    ResourceDescriptor? Resource,
    string Outcome,
    DateTimeOffset Timestamp,
    CorrelationId CorrelationId);
