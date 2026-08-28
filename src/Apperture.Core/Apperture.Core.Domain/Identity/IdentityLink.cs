namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Links a User to an authentication source (Entra or local). Value object / entity.
/// A link with a null <paramref name="LastAuthenticatedAt"/> is pending: it has been associated but never proven
/// by a successful authentication.
/// </summary>
/// <param name="Source">Authentication source.</param>
/// <param name="ExternalId">Authoritative external identifier: Entra object id (oid), or normalized local username.</param>
/// <param name="LastAuthenticatedAt">Proof marker. Null means pending, non-null means proven.</param>
/// <param name="TenantId">Entra directory (tenant) id the object id belongs to. Null for local links and legacy Entra links.</param>
/// <param name="RegistrationId">Provenance only: the Entra instance registration this link was created against.</param>
/// <param name="LinkedAt">When the link was created.</param>
/// <param name="ProviderSubject">Diagnostic token subject (<c>sub</c>). Never an identity key.</param>
/// <param name="DisplayNameSnapshot">Non-authoritative directory display name at association time.</param>
/// <param name="UserPrincipalNameSnapshot">Non-authoritative directory UPN at association time.</param>
/// <param name="MailSnapshot">Non-authoritative directory mail at association time.</param>
public sealed record IdentityLink(
    IdentitySource Source,
    string ExternalId,
    DateTimeOffset? LastAuthenticatedAt,
    string? TenantId = null,
    string? RegistrationId = null,
    DateTimeOffset? LinkedAt = null,
    string? ProviderSubject = null,
    string? DisplayNameSnapshot = null,
    string? UserPrincipalNameSnapshot = null,
    string? MailSnapshot = null)
{
    /// <summary>True when a successful authentication has proven this link belongs to the user.</summary>
    public bool IsProven => LastAuthenticatedAt is not null;
}
