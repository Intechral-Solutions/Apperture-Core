using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// What an Entra authentication was for. The linking purpose is carried through the OIDC challenge in
/// <c>AuthenticationProperties</c>; everything else is a normal sign-in.
/// </summary>
public enum EntraAuthenticationPurpose
{
    SignIn,
    Link
}

/// <summary>
/// The identity a validated Entra token asserts. <see cref="ObjectId"/> plus <see cref="TenantId"/> is the
/// only identity key; the remaining values are diagnostics or non-authoritative display snapshots.
/// </summary>
public sealed record EntraIdentityAssertion(
    string ObjectId,
    string TenantId,
    string? Subject = null,
    string? DisplayName = null,
    string? UserPrincipalName = null,
    string? Mail = null);

/// <summary>
/// Result of completing an Entra authentication. On failure the caller must not issue an application
/// cookie, so a failed link attempt leaves the operator's existing local session intact.
/// </summary>
public sealed record EntraAssociationOutcome(bool Succeeded, UserId? UserId, string? FailureCode)
{
    public static EntraAssociationOutcome Success(UserId userId) => new(true, userId, null);

    public static EntraAssociationOutcome Failure(string failureCode) => new(false, null, failureCode);
}

/// <summary>
/// Binds Microsoft Entra identities to existing platform users and proves them. Never creates a user.
/// After a link becomes proven, <see cref="IEntraPrimaryAuthenticationPolicy"/> disables local password
/// sign-in for ordinary users (hash retained). Pending administrator associations do not disable local login.
/// </summary>
public interface IEntraIdentityAssociationService
{
    /// <summary>
    /// Completes a normal Entra sign-in: resolves the object id to its platform user, proves a pending link,
    /// and completes legacy tenant metadata when the token's directory matches the active registration.
    /// </summary>
    ValueTask<EntraAssociationOutcome> CompleteSignInAsync(
        EntraIdentityAssertion assertion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a self-service link for an operator who is already signed in locally. Idempotent when the
    /// same identity is already linked to that user.
    /// </summary>
    ValueTask<EntraAssociationOutcome> CompleteLinkAsync(
        UserId initiatingUserId,
        EntraIdentityAssertion assertion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a pending (unproven) Entra link for an administrator association, filling tenant and
    /// registration provenance from the active registration and best-effort directory snapshots.
    /// </summary>
    ValueTask<IdentityLink> BuildPendingAdminLinkAsync(
        string objectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="BuildPendingAdminLinkAsync(string,CancellationToken)"/> against a specific
    /// registration (directory picker), not only the OIDC-active registration.
    /// </summary>
    ValueTask<IdentityLink> BuildPendingAdminLinkAsync(
        string objectId,
        EntraInstanceRegistration registration,
        CancellationToken cancellationToken = default) =>
        BuildPendingAdminLinkAsync(objectId, cancellationToken);
}
