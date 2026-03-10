namespace Apperture.Core.Security.Trust;

/// <summary>
/// Read-only view of an enrollment token record for admin/validation queries.
/// Does not expose the raw token value.
/// </summary>
public sealed record EnrollmentTokenRecord(
    Guid Id,
    string RuntimeId,
    DateTimeOffset ExpiresAt,
    bool Used,
    DateTimeOffset CreatedAt);

/// <summary>
/// Result of validating an enrollment token without consuming it.
/// </summary>
public sealed record EnrollmentTokenValidationResult(
    bool IsValid,
    DateTimeOffset? ExpiresAt = null,
    string? FailureReason = null);

/// <summary>
/// Registration payload for a runtime identity during enrollment.
/// </summary>
public sealed record RuntimeIdentityRegistration(
    string RuntimeName,
    string CredentialType,
    string? PublicKeyOrThumbprint,
    string? ManagedIdentityPrincipalId);

/// <summary>
/// Structured result of runtime trust validation.
/// </summary>
public sealed record RuntimeTrustValidationResult(
    bool IsValid,
    string? FailureReason = null,
    IReadOnlyDictionary<string, string>? Claims = null);

/// <summary>
/// Request to roll over a runtime credential (e.g. change public key or thumbprint).
/// </summary>
public sealed record RuntimeCredentialRolloverRequest(
    string RuntimeId,
    string NewCredentialType,
    string? NewPublicKeyOrThumbprint,
    string? NewManagedIdentityPrincipalId,
    string? Reason = null);

/// <summary>
/// Audit record for enrollment and runtime trust operations.
/// Implementations may persist these to a dedicated enrollment audit store.
/// </summary>
public sealed record EnrollmentAuditRecord(
    string EventType,
    string RuntimeId,
    DateTimeOffset OccurredAtUtc,
    string? Actor = null,
    string? Details = null);

/// <summary>
/// Writes enrollment and runtime trust audit records.
/// Implementations live in the Control Plane; Core only defines the contract.
/// </summary>
public interface IEnrollmentAuditWriter
{
    ValueTask WriteAsync(EnrollmentAuditRecord record, CancellationToken cancellationToken = default);
}

