namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// Paged audit records for secret operations.
/// </summary>
public sealed record SecretAuditPage(
    IReadOnlyList<SecretAuditRecord> Records,
    string? ContinuationToken = null);

/// <summary>
/// Minimal audit event model for secret operations.
/// </summary>
public sealed record SecretAuditRecord(
    DateTimeOffset Timestamp,
    string EventType,
    SecretId SecretId,
    string? Version,
    string Actor,
    string Outcome);
