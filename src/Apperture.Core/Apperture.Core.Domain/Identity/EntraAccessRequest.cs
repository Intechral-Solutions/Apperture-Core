namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Lifecycle of a self-service Microsoft Entra access request. Historical rows are retained.
/// </summary>
public enum EntraAccessRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>
/// An unprovisioned Entra identity asking for an Apperture account. Identity is
/// <c>TenantId + ProviderObjectId</c>; snapshots are presentation/history only.
/// </summary>
public sealed record EntraAccessRequest(
    string Id,
    string RegistrationId,
    string TenantId,
    string ProviderObjectId,
    string? DisplayNameSnapshot,
    string? UserPrincipalNameSnapshot,
    string? MailSnapshot,
    string? UserTypeSnapshot,
    DateTimeOffset AuthenticatedAtUtc,
    DateTimeOffset RequestedAtUtc,
    EntraAccessRequestStatus Status,
    DateTimeOffset? ReviewedAtUtc = null,
    string? ReviewedByUserId = null,
    string? ProvisionedUserId = null);

/// <summary>Local Apperture status of a directory object, never derived from email or UPN.</summary>
public enum EntraDirectoryAppertureStatus
{
    Available = 0,
    Provisioned = 1,
    AccessRequestPending = 2
}
