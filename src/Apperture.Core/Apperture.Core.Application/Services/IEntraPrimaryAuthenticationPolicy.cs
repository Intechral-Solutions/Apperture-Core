using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Entra-primary authentication policy: after an Entra identity is proven, local password
/// sign-in is disabled. A designated emergency Platform Administrator is never auto-disabled.
/// A Platform Administrator's local credential is also preserved while disabling it would leave
/// no ordinary Platform Administrator able to sign in, or, until healthy emergency access exists,
/// no ordinary Platform Administrator able to sign in without Entra (the administrator-viability
/// invariants, decided under the global administrator-set gate).
/// </summary>
public interface IEntraPrimaryAuthenticationPolicy
{
    /// <summary>
    /// Applies the policy after successful Entra authentication of a proven identity. Call this on
    /// every successful Entra sign-in, not only the first proof: a previously deferred last-admin
    /// disable is re-evaluated when another Entra-independent Platform Administrator exists.
    /// Idempotent for already-disabled credentials: they are left unchanged and are not re-audited.
    /// A deferral or a busy result leaves the credential enabled and is not a sign-in failure: the next
    /// successful Entra sign-in applies the policy again.
    /// </summary>
    ValueTask<EntraPrimaryLocalDisableOutcome> ApplyAfterEntraProvenAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Presentation of Entra association and local sign-in policy for Account Security and user administration.
    /// </summary>
    ValueTask<LocalAuthenticationPresentation> DescribeAsync(
        User user,
        LocalCredentialData? credential,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of attempting to disable local authentication after Entra proof.</summary>
public enum EntraPrimaryLocalDisableOutcome
{
    NoLocalCredential,
    AlreadyDisabled,
    Disabled,

    /// <summary>
    /// Deferred by I-FALLBACK: the change would leave no ordinary Platform Administrator able to sign in without Entra while
    /// Recovery Access cannot stand in. Recorded as a deferral attempt.
    /// </summary>
    DeferredLastEntraIndependentPlatformAdministrator,
    ProtectedEmergencyAccount,

    /// <summary>
    /// Deferred by I-VIABLE: the change would leave no ordinary Platform Administrator able to sign in at all, because the
    /// user's own Entra link is not a usable way in (pending, or for a directory the sign-in does not accept). Healthy Recovery
    /// Access does not change this. Recorded as the same deferral attempt as
    /// <see cref="DeferredLastEntraIndependentPlatformAdministrator"/>: the user is the last Entra-independent administrator.
    /// </summary>
    DeferredLastViablePlatformAdministrator,

    /// <summary>
    /// Deferred because the sign-in configuration the decision depends on could not be established, so whether the change is
    /// safe is not known. No attempt row is written (nothing is proven about the administrators); the caller logs it.
    /// </summary>
    DeferredAdministratorViabilityUnavailable,

    /// <summary>
    /// Another change to administrator access held the global gate past its bound. Nothing was decided and nothing changed:
    /// the credential stays enabled, no row is written, and the next successful Entra sign-in applies the policy again.
    /// </summary>
    NotAppliedBusy,

    /// <summary>
    /// The change was refused, but not because of this user: administrator access changed between the two reads of the decision
    /// (an exempt writer such as the consumption of the last Recovery Access recovery code, or a writer not yet under the gate), and
    /// the state read under the gate does not show the user as an Entra-independent administrator. The credential stays enabled and
    /// no attempt row is written, because the last-administrator row would not be true of this user; the caller logs it. The next
    /// successful Entra sign-in applies the policy again.
    /// </summary>
    DeferredConcurrentAdministratorAccessChange,
}

/// <summary>
/// Operator-facing authentication-method state. Values are stable API strings.
/// </summary>
public sealed record LocalAuthenticationPresentation(
    string EntraAssociationStatus,
    string LocalSignInPolicy,
    bool RecoverySafeguardActive,
    bool EmergencyAccessAccount = false);

/// <summary>Stable status strings for Entra association and local sign-in policy.</summary>
public static class AuthenticationMethodStatuses
{
    public const string EntraNone = "none";
    public const string EntraPending = "pending";
    public const string EntraProven = "proven";

    public const string LocalNone = "none";
    public const string LocalEnabled = "enabled";
    public const string LocalDisabled = "disabled";
    public const string LocalRecoverySafeguard = "recovery_safeguard";
    public const string LocalEmergencyAccess = "emergency_access";
}
